using LecturIA.Core.Models;
using LecturIA.Core.Recording;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Resolves the output path for a student's recording, ensuring no collision
/// with previously generated files.
/// </summary>
public interface IRecordingPathResolver
{
    /// <summary>Root folder where recordings are persisted.</summary>
    string RecordingsFolder { get; }

    /// <summary>
    /// Returns a non-existing path for the given student in the requested
    /// container format. When previous recordings exist, a numeric suffix is
    /// appended to avoid overwriting them.
    /// </summary>
    string ResolveFor(Student student, AudioFormat format);
}
