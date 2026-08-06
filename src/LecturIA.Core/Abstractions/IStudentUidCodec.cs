using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Encodes a student into a deterministic, opaque UID string and decodes
/// the UID back into the original student.
/// </summary>
/// <remarks>
/// The UID protects student metadata in the persistent course list and
/// remains readable for compatibility with legacy recording file names.
/// New recording file names use a separate RUT-only pseudonym.
/// </remarks>
public interface IStudentUidCodec
{
    /// <summary>
    /// Produces an opaque, filename-safe UID for the given student.
    /// Repeated calls with the same student return the same UID.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The student does not have a non-empty RUT, or any field exceeds
    /// the maximum length supported by the codec.
    /// </exception>
    string Encode(Student student);

    /// <summary>
    /// Recovers the original student metadata from a UID produced by
    /// <see cref="Encode"/>.
    /// </summary>
    /// <exception cref="FormatException">
    /// The string is not a valid UID (wrong length or invalid characters).
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The UID failed authentication. The active UID key does not match
    /// the one used to produce the UID, or the UID was tampered with.
    /// </exception>
    Student Decode(string uid);
}
