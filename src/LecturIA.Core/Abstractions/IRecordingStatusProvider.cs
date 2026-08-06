using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Tracks which students have at least one completed encrypted recording.
/// </summary>
public interface IRecordingStatusProvider
{
    /// <summary>Raised after a file-system change rebuilds the completion index.</summary>
    event EventHandler? StatusChanged;

    /// <summary>
    /// Rebuilds the completion index from finalized recording files on disk.
    /// Files that are incomplete, unreadable, or use an unknown format are ignored.
    /// </summary>
    void Refresh();

    /// <summary>Returns whether the student has a completed recording in the active folder.</summary>
    bool HasCompletedRecording(Student student);

    /// <summary>
    /// Deletes older recordings for the student after a new recording has
    /// completed successfully, retaining <paramref name="latestFilePath"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is outside the active recording folder or does not belong to the student.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The latest file is missing or is not a finalized recording container.
    /// </exception>
    /// <exception cref="IOException">One or more older recordings could not be deleted.</exception>
    void RetainOnlyLatestRecording(Student student, string latestFilePath);
}
