using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// Deterministic authenticated encryption codec that turns a
/// <see cref="Student"/> into an opaque, filename-safe UID and back.
/// </summary>
/// <remarks>
/// <para>
/// The construction is a two-key SIV-style scheme built on primitives
/// that ship with .NET 8: HMAC-SHA256 acts as the synthetic IV plus the
/// authentication tag, and AES-256 in counter mode (implemented via the
/// native ECB primitive) provides the cipher.
/// </para>
/// <para>
/// Encryption:
/// <list type="number">
///   <item><c>K_enc = SHA-256("LECTURIA-UID-ENC" || K_uid)</c></item>
///   <item><c>K_mac = SHA-256("LECTURIA-UID-MAC" || K_uid)</c></item>
///   <item><c>tag   = HMAC-SHA256(K_mac, payload)[:16]</c></item>
///   <item><c>ct    = AES-CTR(K_enc, IV=tag, payload)</c></item>
///   <item><c>UID   = base32(tag || ct)</c></item>
/// </list>
/// Decryption recomputes <c>tag</c> from the recovered plaintext and
/// rejects the UID with <see cref="CryptographicException"/> if the
/// freshly computed tag does not match the one in the input.
/// </para>
/// <para>
/// The construction is deterministic by design (same student always
/// yields the same UID), authenticated (any modification of the UID is
/// detected at decode time), and uses only audited primitives from the
/// .NET base class library.
/// </para>
/// </remarks>
public sealed class SivStudentUidCodec : IStudentUidCodec
{
    private const int TagLength = 16;
    private const int FieldLengthByte = 1;
    private const int MaxFieldLength = byte.MaxValue;

    private static ReadOnlySpan<byte> EncLabel => "LECTURIA-UID-ENC"u8;
    private static ReadOnlySpan<byte> MacLabel => "LECTURIA-UID-MAC"u8;

    private static readonly CultureInfo ChileanSpanish = new("es-CL");

    private readonly byte[] _kEnc;
    private readonly byte[] _kMac;

    /// <summary>
    /// Creates a codec that uses subkeys derived from the supplied
    /// <see cref="IStudentUidKeyProvider"/>.
    /// </summary>
    public SivStudentUidCodec(IStudentUidKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _kEnc = DeriveSubkey(keyProvider.Key.Span, EncLabel);
        _kMac = DeriveSubkey(keyProvider.Key.Span, MacLabel);
    }

    /// <inheritdoc />
    public string Encode(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        var rut = NormalizeRut(student.Rut);
        if (string.IsNullOrEmpty(rut))
        {
            throw new ArgumentException(
                "Student has no RUT. Records without RUT are not allowed.",
                nameof(student));
        }

        var firstName = NormalizeName(student.FirstName);
        var lastName = NormalizeName(student.LastName);

        var payload = SerializePayload(rut, firstName, lastName);

        Span<byte> tag = stackalloc byte[TagLength];
        ComputeTag(payload, tag);

        var output = new byte[TagLength + payload.Length];
        tag.CopyTo(output);

        // AES-CTR uses the tag as the initial counter block; we encrypt
        // payload into the second half of the output buffer.
        AesCtrXor(_kEnc, tag, payload, output.AsSpan(TagLength));

        return Base32.Encode(output);
    }

    /// <inheritdoc />
    public Student Decode(string uid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        var bytes = Base32.Decode(uid);
        if (bytes.Length <= TagLength)
        {
            throw new FormatException("UID is too short to contain an authenticated payload.");
        }

        var tag = bytes.AsSpan(0, TagLength);
        var ciphertext = bytes.AsSpan(TagLength);
        var plaintext = new byte[ciphertext.Length];

        AesCtrXor(_kEnc, tag, ciphertext, plaintext);

        Span<byte> expectedTag = stackalloc byte[TagLength];
        ComputeTag(plaintext, expectedTag);
        if (!CryptographicOperations.FixedTimeEquals(tag, expectedTag))
        {
            throw new CryptographicException(
                "UID authentication tag did not match. The UID is corrupt or the active UID key is wrong.");
        }

        var (rut, firstName, lastName) = DeserializePayload(plaintext);
        return new Student(rut, firstName, lastName);
    }

    private void ComputeTag(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        Span<byte> full = stackalloc byte[32];
        HMACSHA256.HashData(_kMac, payload, full);
        full[..TagLength].CopyTo(destination);
    }

    private static byte[] DeriveSubkey(ReadOnlySpan<byte> root, ReadOnlySpan<byte> label)
    {
        var combined = new byte[label.Length + root.Length];
        label.CopyTo(combined);
        root.CopyTo(combined.AsSpan(label.Length));
        try
        {
            return SHA256.HashData(combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined);
        }
    }

    private static byte[] SerializePayload(string rut, string firstName, string lastName)
    {
        var rutBytes = Encoding.UTF8.GetBytes(rut);
        var firstBytes = Encoding.UTF8.GetBytes(firstName);
        var lastBytes = Encoding.UTF8.GetBytes(lastName);

        EnsureFitsInByte(rutBytes.Length, "RUT");
        EnsureFitsInByte(firstBytes.Length, "first name");
        EnsureFitsInByte(lastBytes.Length, "last name");

        var totalLength =
            FieldLengthByte + rutBytes.Length
            + FieldLengthByte + firstBytes.Length
            + FieldLengthByte + lastBytes.Length;

        var payload = new byte[totalLength];
        var offset = 0;

        payload[offset++] = (byte)rutBytes.Length;
        rutBytes.CopyTo(payload.AsSpan(offset));
        offset += rutBytes.Length;

        payload[offset++] = (byte)firstBytes.Length;
        firstBytes.CopyTo(payload.AsSpan(offset));
        offset += firstBytes.Length;

        payload[offset++] = (byte)lastBytes.Length;
        lastBytes.CopyTo(payload.AsSpan(offset));

        return payload;
    }

    private static (string Rut, string FirstName, string LastName) DeserializePayload(ReadOnlySpan<byte> payload)
    {
        var rut = ReadField(ref payload);
        var firstName = ReadField(ref payload);
        var lastName = ReadField(ref payload);
        if (!payload.IsEmpty)
        {
            throw new FormatException("Decoded payload contains unexpected trailing bytes.");
        }

        return (rut, firstName, lastName);
    }

    private static string ReadField(ref ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1)
        {
            throw new FormatException("Decoded payload is truncated at a field length byte.");
        }

        var length = payload[0];
        payload = payload[1..];
        if (payload.Length < length)
        {
            throw new FormatException("Decoded payload is truncated within a field.");
        }

        var value = Encoding.UTF8.GetString(payload[..length]);
        payload = payload[length..];
        return value;
    }

    private static void EnsureFitsInByte(int length, string fieldName)
    {
        if (length > MaxFieldLength)
        {
            throw new ArgumentException(
                $"Field '{fieldName}' exceeds the maximum supported length of {MaxFieldLength} bytes.");
        }
    }

    private static string NormalizeRut(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var index = 0;
        foreach (var c in raw)
        {
            if (c is '.' or ' ')
            {
                continue;
            }

            buffer[index++] = char.ToUpperInvariant(c);
        }

        return new string(buffer[..index]);
    }

    private static string NormalizeName(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : raw.Trim().ToUpper(ChileanSpanish);
    }

    /// <summary>
    /// AES-256 in counter mode. Implemented on top of native AES-ECB
    /// because <see cref="System.Security.Cryptography.Aes"/> in .NET 8
    /// does not expose CTR directly. The initial counter block is the
    /// 16-byte synthetic IV; subsequent blocks increment the last 4 bytes
    /// (32-bit big-endian counter), which is sufficient for the small
    /// payload sizes the codec produces.
    /// </summary>
    private static void AesCtrXor(byte[] key, ReadOnlySpan<byte> initialCounter, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length != output.Length)
        {
            throw new ArgumentException("Input and output buffers must have the same length.");
        }

        if (input.IsEmpty)
        {
            return;
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> counter = stackalloc byte[16];
        Span<byte> keystream = stackalloc byte[16];
        initialCounter.CopyTo(counter);

        var transform = aes.CreateEncryptor();
        try
        {
            var counterArray = counter.ToArray();
            var keystreamArray = new byte[16];

            var processed = 0;
            while (processed < input.Length)
            {
                // ICryptoTransform requires byte arrays; copy the counter
                // into a working array, encrypt, then XOR into the output.
                counter.CopyTo(counterArray);
                _ = transform.TransformBlock(counterArray, 0, 16, keystreamArray, 0);

                var blockLength = Math.Min(16, input.Length - processed);
                for (var i = 0; i < blockLength; i++)
                {
                    output[processed + i] = (byte)(input[processed + i] ^ keystreamArray[i]);
                }

                processed += blockLength;
                IncrementCounter(counter);
            }

            CryptographicOperations.ZeroMemory(keystreamArray);
        }
        finally
        {
            transform.Dispose();
        }
    }

    private static void IncrementCounter(Span<byte> counter)
    {
        // Treat the last 4 bytes as a big-endian 32-bit counter.
        var counterValue = BinaryPrimitives.ReadUInt32BigEndian(counter[12..]);
        counterValue++;
        BinaryPrimitives.WriteUInt32BigEndian(counter[12..], counterValue);
    }
}
