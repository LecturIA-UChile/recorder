using System.Security.Cryptography;
using System.Text;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// Produces a versioned, keyed pseudonym from a normalized student RUT.
/// </summary>
/// <remarks>
/// HMAC prevents an observer who only has the recordings folder from testing
/// likely RUT values. The version prefix allows future identifier migrations
/// without confusing new files with legacy reversible student UIDs.
/// </remarks>
public sealed class HmacStudentRecordingIdProvider : IStudentRecordingIdProvider, IDisposable
{
    internal const string Prefix = "R1-";

    private static ReadOnlySpan<byte> KeyLabel => "LECTURIA-RECORDING-RUT-ID"u8;

    private readonly byte[] _key;
    private bool _disposed;

    /// <summary>Creates the provider from the application's UID master key.</summary>
    public HmacStudentRecordingIdProvider(IStudentUidKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _key = HMACSHA256.HashData(keyProvider.Key.Span, KeyLabel);
    }

    /// <inheritdoc />
    public string GetId(Student student)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(student);

        var normalizedRut = NormalizeRut(student.Rut);
        if (normalizedRut.Length == 0)
        {
            throw new ArgumentException(
                "Student has no RUT. Recordings require a stable primary identifier.",
                nameof(student));
        }

        var rutBytes = Encoding.UTF8.GetBytes(normalizedRut);
        try
        {
            var digest = HMACSHA256.HashData(_key, rutBytes);
            return Prefix + Base32.Encode(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rutBytes);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private static string NormalizeRut(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var index = 0;
        foreach (var character in raw)
        {
            if (character is '.' or ' ')
            {
                continue;
            }

            buffer[index++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..index]);
    }
}
