using System.Security.Cryptography;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Exposes the RSA public key used by the application to wrap recording
/// content keys.
/// </summary>
/// <remarks>
/// Implementations are expected to be long-lived (registered as singletons
/// in DI). The returned <see cref="RSA"/> is owned by the provider and
/// must not be disposed by callers.
/// </remarks>
public interface IPublicKeyProvider
{
    /// <summary>
    /// Public key used to encrypt per-recording AES content keys via
    /// RSA-OAEP-SHA256.
    /// </summary>
    RSA Key { get; }

    /// <summary>
    /// First bytes of the SHA-256 of the key's SubjectPublicKeyInfo
    /// encoding. Written into every encrypted file so the decryption tool
    /// can pick the matching private key when several have been issued
    /// over time. Length is fixed by
    /// <see cref="LecturIA.Core.Crypto.EncryptedRecordingFormat.FingerprintLength"/>.
    /// </summary>
    ReadOnlyMemory<byte> Fingerprint { get; }
}
