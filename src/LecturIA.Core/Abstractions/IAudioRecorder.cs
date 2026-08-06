using LecturIA.Core.Recording;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Captures audio from a selected input device and persists it to disk.
/// </summary>
public interface IAudioRecorder : IAsyncDisposable
{
    /// <summary>Current state of the recorder.</summary>
    RecordingState State { get; }

    /// <summary>Maximum duration retained by an input test.</summary>
    TimeSpan InputTestMaximumDuration { get; }

    /// <summary>Whether a captured input-test sample is available for playback.</summary>
    bool HasInputTestRecording { get; }

    /// <summary>Whether the latest recording from this application session is available in memory.</summary>
    bool HasLatestSessionRecording { get; }

    /// <summary>Duration of the latest session recording available for playback.</summary>
    TimeSpan LatestSessionRecordingDuration { get; }

    /// <summary>Current playback position within the latest session recording.</summary>
    TimeSpan LatestSessionRecordingPosition { get; }

    /// <summary>Raised whenever playback of the latest session recording starts or stops.</summary>
    event EventHandler<bool>? SessionRecordingPlaybackStateChanged;

    /// <summary>Raised whenever the recorder transitions to a new state.</summary>
    event EventHandler<RecordingState>? StateChanged;

    /// <summary>Raised with a normalized peak level while an input test is active.</summary>
    event EventHandler<float>? InputLevelChanged;

    /// <summary>Raised when playback of the input-test sample starts or stops.</summary>
    event EventHandler<bool>? InputTestPlaybackStateChanged;

    /// <summary>Returns the audio input devices currently available.</summary>
    IReadOnlyList<AudioInputDevice> GetInputDevices();

    /// <summary>
    /// Starts capturing a short in-memory sample and measuring its input level.
    /// </summary>
    /// <param name="deviceNumber">Index of the input device to test.</param>
    /// <exception cref="InvalidOperationException">Another audio operation is active.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The device index is not available.</exception>
    void StartInputTest(int deviceNumber);

    /// <summary>Stops the active input test and retains its sample in memory.</summary>
    void StopInputTest();

    /// <summary>Plays the retained input-test sample through the default output device.</summary>
    /// <exception cref="InvalidOperationException">No sample is available or another audio operation is active.</exception>
    void PlayInputTestRecording();

    /// <summary>Stops playback of the input-test sample.</summary>
    void StopInputTestPlayback();

    /// <summary>Discards the retained input-test sample.</summary>
    void ClearInputTestRecording();

    /// <summary>Plays the latest successfully completed recording retained in memory.</summary>
    /// <exception cref="InvalidOperationException">
    /// No session recording is available or another audio operation is active.
    /// </exception>
    void PlayLatestSessionRecording();

    /// <summary>Stops playback of the latest session recording.</summary>
    void StopLatestSessionRecordingPlayback();

    /// <summary>Discards the latest session recording retained in memory.</summary>
    void ClearLatestSessionRecording();

    /// <summary>
    /// Starts recording from the selected device, writing the audio data to
    /// <paramref name="outputFilePath"/> as MP3.
    /// </summary>
    /// <param name="outputFilePath">Absolute path of the file to create.</param>
    /// <param name="inputDeviceNumber">Index of the input device to capture.</param>
    /// <param name="metadata">
    /// Optional descriptive metadata written into the encrypted recording header.
    /// When <see langword="null"/>, no metadata block is written.
    /// </param>
    /// <exception cref="InvalidOperationException">Another audio operation is active.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The device index is not available.</exception>
    void Start(
        string outputFilePath,
        int inputDeviceNumber,
        RecordingMetadata? metadata = null);

    /// <summary>
    /// Stops the active recording and returns the resulting file path and duration.
    /// </summary>
    /// <exception cref="InvalidOperationException">No recording is currently active.</exception>
    Task<RecordingResult> StopAsync();
}
