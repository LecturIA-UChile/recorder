using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using LecturIA.Core.Abstractions;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// Deterministic authenticated encryption codec that encodes a course
/// triple (school, level, section) into an opaque UID and decodes it back.
/// </summary>
/// <remarks>
/// Uses the same two-key SIV-style construction as
/// <see cref="SivStudentUidCodec"/> with course-specific domain labels so
/// that course UIDs and student UIDs are cryptographically independent
/// even though they share the same master key material.
/// </remarks>
public sealed class SivCourseUidCodec : ICourseUidCodec
{
    private const int TagLength = 16;
    private const int FieldLengthByte = 2;
    private const int MaxFieldLength = ushort.MaxValue;

    private static ReadOnlySpan<byte> EncLabel => "LECTURIA-COURSE-ENC"u8;
    private static ReadOnlySpan<byte> MacLabel => "LECTURIA-COURSE-MAC"u8;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly byte[] _kEnc;
    private readonly byte[] _kMac;

    /// <summary>
    /// Creates a codec that uses subkeys derived from the supplied
    /// <see cref="IStudentUidKeyProvider"/>. The same master key is used
    /// for both student and course UIDs; domain separation is provided
    /// by distinct labels in the subkey derivation.
    /// </summary>
    public SivCourseUidCodec(IStudentUidKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _kEnc = DeriveSubkey(keyProvider.Key.Span, EncLabel);
        _kMac = DeriveSubkey(keyProvider.Key.Span, MacLabel);
    }

    /// <inheritdoc />
    public string Encode(string school, string level, string section)
    {
        var payload = SerializePayload(
            Normalize(school),
            Normalize(level),
            Normalize(section));

        Span<byte> tag = stackalloc byte[TagLength];
        ComputeTag(payload, tag);

        var output = new byte[TagLength + payload.Length];
        tag.CopyTo(output);
        AesCtrXor(_kEnc, tag, payload, output.AsSpan(TagLength));

        return Base32.Encode(output);
    }

    /// <inheritdoc />
    public (string School, string Level, string Section) Decode(string uid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        var bytes = Base32.Decode(uid);
        if (bytes.Length <= TagLength)
        {
            throw new FormatException("Course UID is too short to contain an authenticated payload.");
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
                "Course UID authentication tag did not match.");
        }

        return DeserializePayload(plaintext);
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

    private static byte[] SerializePayload(string school, string level, string section)
    {
        var schoolBytes = Encoding.UTF8.GetBytes(school);
        var levelBytes = Encoding.UTF8.GetBytes(level);
        var sectionBytes = Encoding.UTF8.GetBytes(section);

        EnsureFits(schoolBytes.Length, "school");
        EnsureFits(levelBytes.Length, "level");
        EnsureFits(sectionBytes.Length, "section");

        var totalLength =
            FieldLengthByte + schoolBytes.Length
            + FieldLengthByte + levelBytes.Length
            + FieldLengthByte + sectionBytes.Length;

        var payload = new byte[totalLength];
        var offset = 0;

        WriteField(payload, ref offset, schoolBytes);
        WriteField(payload, ref offset, levelBytes);
        WriteField(payload, ref offset, sectionBytes);

        return payload;
    }

    private static void WriteField(byte[] buffer, ref int offset, byte[] value)
    {
        // 2-byte big-endian length prefix (allows empty strings and long names).
        buffer[offset++] = (byte)(value.Length >> 8);
        buffer[offset++] = (byte)(value.Length & 0xFF);
        value.CopyTo(buffer.AsSpan(offset));
        offset += value.Length;
    }

    private static (string School, string Level, string Section) DeserializePayload(ReadOnlySpan<byte> payload)
    {
        var school = ReadField(ref payload);
        var level = ReadField(ref payload);
        var section = ReadField(ref payload);
        if (!payload.IsEmpty)
        {
            throw new FormatException("Course UID payload contains unexpected trailing bytes.");
        }

        return (school, level, section);
    }

    private static string ReadField(ref ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FieldLengthByte)
        {
            throw new FormatException("Course UID payload is truncated at a field length.");
        }

        var length = (payload[0] << 8) | payload[1];
        payload = payload[FieldLengthByte..];
        if (payload.Length < length)
        {
            throw new FormatException("Course UID payload is truncated within a field.");
        }

        var value = Encoding.UTF8.GetString(payload[..length]);
        payload = payload[length..];
        return value;
    }

    private static void EnsureFits(int length, string fieldName)
    {
        if (length > MaxFieldLength)
        {
            throw new ArgumentException(
                $"Field '{fieldName}' exceeds the maximum supported length of {MaxFieldLength} bytes.",
                nameof(fieldName));
        }
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpper(Invariant);

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
        initialCounter.CopyTo(counter);
        var counterArray = counter.ToArray();
        var keystreamArray = new byte[16];

        var transform = aes.CreateEncryptor();
        try
        {
            var processed = 0;
            while (processed < input.Length)
            {
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
        var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(counter[12..]);
        value++;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counter[12..], value);
    }
}
