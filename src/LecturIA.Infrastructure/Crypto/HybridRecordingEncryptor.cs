using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;
using LecturIA.Core.Recording;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// <see cref="IRecordingEncryptor"/> that produces LecturIA encrypted
/// recordings using hybrid RSA-OAEP-SHA256 + AES-256-GCM. See
/// <see cref="EncryptedRecordingFormat"/> for the on-disk layout.
/// </summary>
public sealed class HybridRecordingEncryptor : IRecordingEncryptor
{
    private readonly IPublicKeyProvider _publicKeyProvider;
    private readonly int _chunkSize;

    /// <summary>
    /// Creates an encryptor using the supplied public key and the default
    /// frame size of <see cref="EncryptedRecordingFormat.DefaultChunkSize"/>
    /// bytes.
    /// </summary>
    public HybridRecordingEncryptor(IPublicKeyProvider publicKeyProvider)
        : this(publicKeyProvider, EncryptedRecordingFormat.DefaultChunkSize)
    {
    }

    /// <summary>
    /// Creates an encryptor with a custom frame size. Smaller frames trade
    /// per-frame overhead for finer-grained tamper detection on
    /// decryption.
    /// </summary>
    /// <param name="chunkSize">
    /// Plaintext bytes per frame. Must be at least 1; values above a few
    /// MiB defeat the purpose of streaming and waste memory.
    /// </param>
    public HybridRecordingEncryptor(IPublicKeyProvider publicKeyProvider, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(publicKeyProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        _publicKeyProvider = publicKeyProvider;
        _chunkSize = chunkSize;
    }

    /// <inheritdoc />
    public Stream WrapForWrite(Stream destination, RecordingMetadata? metadata = null) =>
        new HybridEncryptingStream(
            destination,
            _publicKeyProvider,
            _chunkSize,
            metadata: metadata);
}
