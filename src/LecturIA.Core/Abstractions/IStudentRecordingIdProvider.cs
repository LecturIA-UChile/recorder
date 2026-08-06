using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Produces a stable, opaque recording identifier derived only from a
/// student's RUT.
/// </summary>
/// <remarks>
/// The identifier is deterministic and filename-safe. It is intentionally
/// one-way: callers match a roster entry by recomputing its identifier
/// instead of recovering the RUT from a file name.
/// </remarks>
public interface IStudentRecordingIdProvider
{
    /// <summary>Returns the recording identifier for the student's normalized RUT.</summary>
    /// <exception cref="ArgumentException">The student does not have a non-empty RUT.</exception>
    string GetId(Student student);
}
