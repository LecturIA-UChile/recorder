namespace LecturIA.Core.Abstractions;

/// <summary>
/// Exposes the application's UID key, the 32-byte symmetric secret used
/// to derive opaque persistent student UIDs and RUT-only recording identifiers.
/// </summary>
/// <remarks>
/// Implementations are expected to be long-lived (registered as singletons
/// in DI). The returned key bytes are owned by the provider and must not
/// be mutated by callers.
/// </remarks>
public interface IStudentUidKeyProvider
{
    /// <summary>
    /// Master 32-byte UID key. Used to derive enc/mac subkeys for the
    /// deterministic AEAD that produces the student UID.
    /// </summary>
    ReadOnlyMemory<byte> Key { get; }

    /// <summary>
    /// First bytes of the SHA-256 of <see cref="Key"/>. Useful to identify
    /// the active key in logs and audit material without exposing the key
    /// itself.
    /// </summary>
    ReadOnlyMemory<byte> Fingerprint { get; }
}
