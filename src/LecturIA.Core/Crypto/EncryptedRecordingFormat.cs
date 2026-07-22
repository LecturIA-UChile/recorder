namespace LecturIA.Core.Crypto;

/// <summary>
/// On-disk binary format of LecturIA encrypted recordings (file extension
/// <c>.lra</c>, magic <c>LRA1</c>).
/// </summary>
/// <remarks>
/// The format wraps an inner audio stream (today MP3, 16 kHz mono) with
/// hybrid cryptography:
/// <list type="bullet">
/// <item>A fresh AES-256 content key is generated per recording.</item>
/// <item>The content key is wrapped with the owner's RSA public key
/// using OAEP-SHA256 padding.</item>
/// <item>The audio is split into fixed-size frames; each frame is
/// encrypted with AES-256-GCM using a 12-byte nonce composed of an 8-byte
/// random prefix (per file) and a 4-byte big-endian frame counter.</item>
/// <item>The SHA-256 of the file header is bound to every frame as
/// associated data, so frames cannot be spliced between files.</item>
/// <item>An end sentinel (a frame length of zero) marks the end of the
/// stream and lets the reader detect truncation.</item>
/// </list>
/// Layout (version 2):
/// <code>
/// Header:
///   magic            "LRA1"           4 bytes
///   version          0x02             1 byte
///   flags            0x00             1 byte (reserved)
///   pk_fingerprint   SHA-256(SPKI)[:8] 8 bytes
///   wrapped_key_len  uint32 BE        4 bytes
///   wrapped_key      RSA-OAEP-SHA256  wrapped_key_len bytes
///   nonce_prefix     random           8 bytes
///   chunk_size       uint32 BE        4 bytes
///   inner_mime_len   uint16 BE        2 bytes
///   inner_mime       UTF-8            inner_mime_len bytes
///   metadata_len     uint32 BE        4 bytes           (added in v2)
///   metadata         UTF-8 JSON       metadata_len bytes (added in v2; may be 0)
///
/// Frame (zero or more, in order):
///   ct_len           uint32 BE        4 bytes (1..chunk_size)
///   ciphertext       AES-256-GCM      ct_len bytes
///   tag              GCM tag          16 bytes
///
/// End sentinel:
///   ct_len           uint32 BE = 0    4 bytes
/// </code>
/// The metadata block is plaintext (like the MIME type) but bound to the
/// per-frame AAD via the header hash, so it is tamper-evident. It carries
/// non-sensitive descriptive data such as which reading passage was read;
/// the schema is a compact JSON object documented in
/// <c>docs/security/encryption.md</c>. Version 1 files have no metadata
/// block and end the header at <c>inner_mime</c>.
/// </remarks>
public static class EncryptedRecordingFormat
{
    /// <summary>File extension used by encrypted recordings on disk.</summary>
    public const string FileExtension = ".lra";

    /// <summary>Length of the magic marker that prefixes every file.</summary>
    public const int MagicLength = 4;

    /// <summary>
    /// Format version produced by the current implementation. Version 2
    /// adds the metadata block at the end of the header; version 1 files
    /// (no metadata block) remain readable by version-aware tools.
    /// </summary>
    public const byte CurrentVersion = 2;

    /// <summary>
    /// Schema version of the JSON metadata block written in the header.
    /// Emitted as the <c>schema</c> field so readers can evolve the schema
    /// without another container version bump.
    /// </summary>
    public const int MetadataSchemaVersion = 1;

    /// <summary>
    /// Number of bytes of the SHA-256 over the SubjectPublicKeyInfo of the
    /// public key that are written into every file as a fingerprint. Lets
    /// the decryption tool tell apart files produced under different keys
    /// and pick the right private key.
    /// </summary>
    public const int FingerprintLength = 8;

    /// <summary>RSA modulus size used for content key wrapping.</summary>
    public const int RsaKeySizeBits = 4096;

    /// <summary>AES content key size (AES-256).</summary>
    public const int AesKeySizeBytes = 32;

    /// <summary>Total length of every per-frame AES-GCM nonce.</summary>
    public const int NonceLength = 12;

    /// <summary>Random portion of the nonce, generated once per file.</summary>
    public const int NoncePrefixLength = 8;

    /// <summary>Counter portion of the nonce, monotonically increasing per frame.</summary>
    public const int FrameCounterLength = NonceLength - NoncePrefixLength;

    /// <summary>AES-GCM authentication tag length.</summary>
    public const int GcmTagLength = 16;

    /// <summary>Default plaintext size of every frame (64 KiB).</summary>
    public const int DefaultChunkSize = 65536;

    /// <summary>
    /// Maximum number of frames per file. With the default chunk size this
    /// is far above any plausible recording length and acts as a safety
    /// limit against pathological inputs.
    /// </summary>
    public const long MaxFrames = uint.MaxValue - 1;

    /// <summary>MIME type of the inner audio stream produced today.</summary>
    public const string InnerMimeType = "audio/mpeg";

    /// <summary>
    /// Magic marker written at the start of every file. UTF-8 byte literal
    /// avoids depending on string encoding tables at runtime.
    /// </summary>
    public static ReadOnlySpan<byte> Magic => "LRA1"u8;
}
