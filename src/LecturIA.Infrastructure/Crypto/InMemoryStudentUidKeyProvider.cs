using System.Security.Cryptography;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// <see cref="IStudentUidKeyProvider"/> that holds the UID key in memory.
/// </summary>
/// <remarks>
/// The key is supplied at construction time, typically loaded from an
/// embedded resource by the application's composition root.
/// </remarks>
public sealed class InMemoryStudentUidKeyProvider : IStudentUidKeyProvider, IDisposable
{
    private readonly byte[] _key;
    private readonly byte[] _fingerprint;
    private bool _disposed;

    /// <summary>
    /// Imports the supplied 32-byte UID key. The constructor copies the
    /// bytes; the caller is free to zero its source buffer afterwards.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> length is not the expected 32 bytes.
    /// </exception>
    public InMemoryStudentUidKeyProvider(ReadOnlySpan<byte> key)
    {
        if (key.Length != EncryptedRecordingFormat.AesKeySizeBytes)
        {
            throw new ArgumentException(
                $"UID key must be exactly {EncryptedRecordingFormat.AesKeySizeBytes} bytes.",
                nameof(key));
        }

        _key = key.ToArray();
        var hash = SHA256.HashData(_key);
        _fingerprint = hash.AsSpan(0, EncryptedRecordingFormat.FingerprintLength).ToArray();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Key
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Fingerprint
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _fingerprint;
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
}
