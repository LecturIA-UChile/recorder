namespace LecturIA.Core.Recording;

/// <summary>
/// Describes the lifecycle phase of an audio recording session.
/// </summary>
public enum RecordingState
{
    /// <summary>The recorder has been created but no recording is active.</summary>
    Idle,

    /// <summary>A recording is currently capturing audio to disk.</summary>
    Recording,

    /// <summary>The most recent recording finished successfully.</summary>
    Stopped,

    /// <summary>The most recent recording terminated with an error.</summary>
    Error,
}
