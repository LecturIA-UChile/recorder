namespace LecturIA.Core.Recording;

/// <summary>
/// Outcome of a completed recording session.
/// </summary>
/// <param name="FilePath">
/// Absolute path of the encrypted recording file written to disk
/// (LecturIA <c>.lra</c> container holding an MP3 inner stream).
/// </param>
/// <param name="Duration">Wall-clock duration of the recording.</param>
public sealed record RecordingResult(string FilePath, TimeSpan Duration);
