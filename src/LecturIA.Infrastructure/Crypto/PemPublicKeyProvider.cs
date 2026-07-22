using System.Security.Cryptography;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// <see cref="IPublicKeyProvider"/> that loads a single RSA public key from
/// a PEM-encoded SubjectPublicKeyInfo blob.
/// </summary>
/// <remarks>
/// The PEM bytes are usually obtained from an embedded resource of the
/// application binary, so the public key ships with the application and
/// is loaded once at startup.
/// </remarks>
public sealed class PemPublicKeyProvider : IPublicKeyProvider, IDisposable
{
    private readonly RSA _key;
    private readonly byte[] _fingerprint;
    private bool _disposed;

    /// <summary>
    /// Imports the public key from the supplied PEM text.
    /// </summary>
    /// <param name="pem">Full PEM document including BEGIN/END markers.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="pem"/> is empty, malformed, or not a public key.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The PEM cannot be parsed as a SubjectPublicKeyInfo.
    /// </exception>
    public PemPublicKeyProvider(string pem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }

        _key = rsa;
        _fingerprint = ComputeFingerprint(rsa);
    }

    /// <inheritdoc />
    public RSA Key
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

        _key.Dispose();
        _disposed = true;
    }

    private static byte[] ComputeFingerprint(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(spki);
        var fingerprint = new byte[EncryptedRecordingFormat.FingerprintLength];
        Buffer.BlockCopy(hash, 0, fingerprint, 0, fingerprint.Length);
        return fingerprint;
    }
}
