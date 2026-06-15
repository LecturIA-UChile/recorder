using LecturIA.Core.Recording;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Captures audio from the default input device and persists it to disk.
/// </summary>
public interface IAudioRecorder : IAsyncDisposable
{
    /// <summary>Current state of the recorder.</summary>
    RecordingState State { get; }

    /// <summary>Raised whenever the recorder transitions to a new state.</summary>
    event EventHandler<RecordingState>? StateChanged;

    /// <summary>
    /// Starts recording, writing the audio data to <paramref name="outputFilePath"/>
    /// in WAV format.
    /// </summary>
    /// <exception cref="InvalidOperationException">A recording is already in progress.</exception>
    void Start(string outputFilePath);

    /// <summary>
    /// Stops the active recording and returns the resulting file path and duration.
    /// </summary>
    /// <exception cref="InvalidOperationException">No recording is currently active.</exception>
    Task<RecordingResult> StopAsync();
}
