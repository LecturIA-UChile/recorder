using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Produces a stable, opaque recording identifier for a student.
/// </summary>
/// <remarks>
/// When the student carries a provider-assigned <see cref="Student.RecordingId"/>
/// it is used verbatim. Otherwise the identifier is derived one-way from the
/// student's RUT: it is deterministic and filename-safe, and callers match a
/// roster entry by recomputing its identifier instead of recovering the RUT
/// from a file name.
/// </remarks>
public interface IStudentRecordingIdProvider
{
    /// <summary>Returns the recording identifier for the student.</summary>
    /// <exception cref="ArgumentException">
    /// The student has neither a recording identifier nor a non-empty RUT.
    /// </exception>
    string GetId(Student student);
}
