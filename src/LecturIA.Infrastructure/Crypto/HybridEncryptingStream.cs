using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;
using LecturIA.Core.Recording;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// Write-only <see cref="Stream"/> that turns plaintext bytes into the
/// LecturIA encrypted recording format described in
/// <see cref="EncryptedRecordingFormat"/>.
/// </summary>
/// <remarks>
/// The stream is intentionally one-shot and not seekable. It buffers
/// plaintext up to the configured frame size, emits a frame as soon as
/// the buffer is full, and finalizes the container on
/// <see cref="Stream.Dispose()"/> by writing any remaining plaintext as a
/// last short frame followed by the zero-length end sentinel.
/// </remarks>
public sealed class HybridEncryptingStream : Stream
{
    private readonly Stream _destination;
    private readonly bool _leaveOpen;
    private readonly AesGcm _aes;
    private readonly byte[] _noncePrefix;
    private readonly byte[] _aad;
    private readonly byte[] _frameBuffer;
    private readonly byte[] _ciphertextBuffer;
    private readonly byte[] _tagBuffer;
    private readonly byte[] _nonceBuffer;
    private int _frameBufferOffset;
    private uint _frameCounter;
    private bool _disposed;
    private bool _finalized;

    /// <summary>
    /// Wraps <paramref name="destination"/> and writes the encrypted
    /// recording header immediately. The destination must be writable;
    /// disposing this stream finalizes the container and disposes the
    /// destination unless <paramref name="leaveOpen"/> is set.
    /// </summary>
    /// <param name="destination">Underlying writable byte sink.</param>
    /// <param name="publicKeyProvider">Owner of the RSA public key used to wrap the AES content key.</param>
    /// <param name="chunkSize">Plaintext bytes per AEAD frame.</param>
    /// <param name="innerMime">MIME type of the inner audio stream (e.g. <c>audio/mpeg</c>).</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the destination stream is not disposed when this stream is disposed.</param>
    /// <param name="metadata">Optional descriptive metadata written into the header (empty block when <see langword="null"/>).</param>
    public HybridEncryptingStream(
        Stream destination,
        IPublicKeyProvider publicKeyProvider,
        int chunkSize = EncryptedRecordingFormat.DefaultChunkSize,
        string innerMime = EncryptedRecordingFormat.InnerMimeType,
        bool leaveOpen = false,
        RecordingMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(publicKeyProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(innerMime);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        _destination = destination;
        _leaveOpen = leaveOpen;
        _frameBuffer = new byte[chunkSize];
        _ciphertextBuffer = new byte[chunkSize];
        _tagBuffer = new byte[EncryptedRecordingFormat.GcmTagLength];
        _nonceBuffer = new byte[EncryptedRecordingFormat.NonceLength];
        _noncePrefix = new byte[EncryptedRecordingFormat.NoncePrefixLength];

        Span<byte> aesKey = stackalloc byte[EncryptedRecordingFormat.AesKeySizeBytes];
        try
        {
            RandomNumberGenerator.Fill(aesKey);
            RandomNumberGenerator.Fill(_noncePrefix);

            // AesGcm copies the key internally; zeroing the source after
            // construction is safe and recommended.
            _aes = new AesGcm(aesKey, EncryptedRecordingFormat.GcmTagLength);

            var rsa = publicKeyProvider.Key;
            var wrappedKey = new byte[rsa.KeySize / 8];
            var wrappedLength = rsa.Encrypt(aesKey, wrappedKey, RSAEncryptionPadding.OaepSHA256);
            if (wrappedLength != wrappedKey.Length)
            {
                Array.Resize(ref wrappedKey, wrappedLength);
            }

            var headerBytes = BuildHeader(
                publicKeyProvider.Fingerprint.Span,
                wrappedKey,
                _noncePrefix,
                chunkSize,
                innerMime,
                SerializeMetadata(metadata));

            // Bind every frame to the file metadata: tampering with the
            // header (e.g. swapping the wrapped key) makes every frame
            // fail authentication.
            _aad = SHA256.HashData(headerBytes);
            _destination.Write(headerBytes, 0, headerBytes.Length);
        }
        catch
        {
            _aes?.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_disposed && !_finalized;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Intentional: a partial frame is not flushed mid-stream because
        // doing so would produce many small AEAD frames and the consumer
        // (the MP3 encoder) does not need durability between Write calls.
        _destination.Flush();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)offset > (uint)buffer.Length || (uint)count > (uint)(buffer.Length - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finalized)
        {
            throw new InvalidOperationException("The encrypted stream has already been finalized.");
        }

        while (!buffer.IsEmpty)
        {
            var space = _frameBuffer.Length - _frameBufferOffset;
            var copy = Math.Min(space, buffer.Length);
            buffer[..copy].CopyTo(_frameBuffer.AsSpan(_frameBufferOffset));
            _frameBufferOffset += copy;
            buffer = buffer[copy..];

            if (_frameBufferOffset == _frameBuffer.Length)
            {
                EmitFrame();
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            try
            {
                FinalizeContainer();
            }
            finally
            {
                _aes.Dispose();
                if (!_leaveOpen)
                {
                    _destination.Dispose();
                }
            }
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    private void FinalizeContainer()
    {
        if (_finalized)
        {
            return;
        }

        if (_frameBufferOffset > 0)
        {
            EmitFrame();
        }

        Span<byte> sentinel = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sentinel, 0u);
        _destination.Write(sentinel);

        _finalized = true;
    }

    private void EmitFrame()
    {
        if (_frameCounter >= EncryptedRecordingFormat.MaxFrames)
        {
            throw new InvalidOperationException(
                $"Recording exceeded the maximum number of frames ({EncryptedRecordingFormat.MaxFrames}).");
        }

        BuildNonce(_frameCounter);
        var plaintext = _frameBuffer.AsSpan(0, _frameBufferOffset);
        var ciphertext = _ciphertextBuffer.AsSpan(0, _frameBufferOffset);

        _aes.Encrypt(_nonceBuffer, plaintext, ciphertext, _tagBuffer, _aad);

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)_frameBufferOffset);
        _destination.Write(length);
        _destination.Write(ciphertext);
        _destination.Write(_tagBuffer);

        _frameBufferOffset = 0;
        _frameCounter++;
    }

    private void BuildNonce(uint counter)
    {
        Buffer.BlockCopy(_noncePrefix, 0, _nonceBuffer, 0, EncryptedRecordingFormat.NoncePrefixLength);
        BinaryPrimitives.WriteUInt32BigEndian(
            _nonceBuffer.AsSpan(EncryptedRecordingFormat.NoncePrefixLength),
            counter);
    }

    private static byte[] BuildHeader(
        ReadOnlySpan<byte> fingerprint,
        byte[] wrappedKey,
        byte[] noncePrefix,
        int chunkSize,
        string innerMime,
        byte[] metadataBytes)
    {
        if (fingerprint.Length != EncryptedRecordingFormat.FingerprintLength)
        {
            throw new ArgumentException(
                $"Fingerprint must be exactly {EncryptedRecordingFormat.FingerprintLength} bytes.",
                nameof(fingerprint));
        }

        var mimeBytes = Encoding.UTF8.GetBytes(innerMime);
        if (mimeBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Inner MIME type does not fit in 16 bits.", nameof(innerMime));
        }

        var totalLength =
            EncryptedRecordingFormat.MagicLength
            + 1 // version
            + 1 // flags
            + EncryptedRecordingFormat.FingerprintLength
            + 4 // wrapped_key_len
            + wrappedKey.Length
            + EncryptedRecordingFormat.NoncePrefixLength
            + 4 // chunk_size
            + 2 // inner_mime_len
            + mimeBytes.Length
            + 4 // metadata_len
            + metadataBytes.Length;

        var header = new byte[totalLength];
        var span = header.AsSpan();

        EncryptedRecordingFormat.Magic.CopyTo(span);
        span = span[EncryptedRecordingFormat.MagicLength..];

        span[0] = EncryptedRecordingFormat.CurrentVersion;
        span[1] = 0;
        span = span[2..];

        fingerprint.CopyTo(span);
        span = span[EncryptedRecordingFormat.FingerprintLength..];

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)wrappedKey.Length);
        span = span[4..];

        wrappedKey.CopyTo(span);
        span = span[wrappedKey.Length..];

        noncePrefix.CopyTo(span);
        span = span[EncryptedRecordingFormat.NoncePrefixLength..];

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)chunkSize);
        span = span[4..];

        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)mimeBytes.Length);
        span = span[2..];

        mimeBytes.CopyTo(span);
        span = span[mimeBytes.Length..];

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)metadataBytes.Length);
        span = span[4..];

        metadataBytes.CopyTo(span);

        return header;
    }

    /// <summary>
    /// Serializes the recording metadata into the compact JSON object
    /// written in the header, or an empty array when no metadata is given.
    /// </summary>
    private static byte[] SerializeMetadata(RecordingMetadata? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<byte>();
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            // Only the schema and the passage primary key are persisted;
            // title and level are derivable from the id via the governed
            // catalog (thesis/reading-text-id-convention.md), which keeps
            // the header payload small.
            writer.WriteStartObject();
            writer.WriteNumber("schema", EncryptedRecordingFormat.MetadataSchemaVersion);
            writer.WriteString("textId", metadata.TextId);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
