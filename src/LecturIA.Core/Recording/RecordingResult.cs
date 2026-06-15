namespace LecturIA.Core.Recording;

/// <summary>
/// Outcome of a completed recording session.
/// </summary>
/// <param name="FilePath">Absolute path of the WAV file written to disk.</param>
/// <param name="Duration">Wall-clock duration of the recording.</param>
public sealed record RecordingResult(string FilePath, TimeSpan Duration);
