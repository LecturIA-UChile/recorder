namespace LecturIA.Core.Abstractions;

/// <summary>
/// Encodes course metadata (school, level, section) into a deterministic
/// opaque UID and decodes the UID back to the original strings.
/// </summary>
/// <remarks>
/// Course metadata is not personally identifiable on its own, but storing
/// it in plain text alongside student UIDs could allow correlation across
/// files. This codec applies the same deterministic AEAD construction used
/// for student UIDs so that <c>courses.json</c> contains no plain text
/// that reveals the originating institution.
/// </remarks>
public interface ICourseUidCodec
{
    /// <summary>
    /// Produces an opaque, filename-safe UID for the given course triple.
    /// Repeated calls with equal inputs return the same UID.
    /// </summary>
    string Encode(string school, string level, string section);

    /// <summary>
    /// Recovers the original course triple from a UID produced by
    /// <see cref="Encode"/>.
    /// </summary>
    /// <exception cref="FormatException">
    /// The string is not a valid UID (wrong length or invalid characters).
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The UID failed authentication.
    /// </exception>
    (string School, string Level, string Section) Decode(string uid);
}
