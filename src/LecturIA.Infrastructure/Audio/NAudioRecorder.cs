using System.Diagnostics;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Recording;

using NAudio.Wave;

namespace LecturIA.Infrastructure.Audio;

/// <summary>
/// Implementation of <see cref="IAudioRecorder"/> built on top of NAudio
/// (<see cref="WaveInEvent"/> + <see cref="WaveFileWriter"/>).
/// </summary>
/// <remarks>
/// The recorder captures mono audio at 16 kHz / 16-bit, a format that keeps
/// file size small while remaining compatible with downstream speech models
/// (Whisper and similar) that expect a 16 kHz mono input.
/// </remarks>
public sealed class NAudioRecorder : IAudioRecorder
{
    private const int SampleRateHz = 16_000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _outputPath;
    private Stopwatch? _stopwatch;
    private RecordingState _state = RecordingState.Idle;
    private TaskCompletionSource<RecordingResult>? _stopCompletion;

    /// <inheritdoc />
    public RecordingState State => _state;

    /// <inheritdoc />
    public event EventHandler<RecordingState>? StateChanged;

    /// <inheritdoc />
    public void Start(string outputFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        if (_state == RecordingState.Recording)
        {
            throw new InvalidOperationException("A recording is already in progress.");
        }

        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _outputPath = outputFilePath;
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRateHz, BitsPerSample, Channels),
            BufferMilliseconds = 50,
        };
        _writer = new WaveFileWriter(outputFilePath, _waveIn.WaveFormat);

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        _stopwatch = Stopwatch.StartNew();
        _waveIn.StartRecording();
        TransitionTo(RecordingState.Recording);
    }

    /// <inheritdoc />
    public Task<RecordingResult> StopAsync()
    {
        if (_state != RecordingState.Recording || _waveIn is null)
        {
            throw new InvalidOperationException("No recording is currently active.");
        }

        _stopCompletion = new TaskCompletionSource<RecordingResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _waveIn.StopRecording();
        return _stopCompletion.Task;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_state == RecordingState.Recording)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // Errors during disposal are suppressed to avoid masking the
                // original failure that triggered the dispose.
            }
        }

        DisposeNativeResources();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            _stopCompletion?.TrySetException(ex);
            TransitionTo(RecordingState.Error);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopwatch?.Stop();
        var duration = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        var path = _outputPath;

        DisposeNativeResources();

        if (e.Exception is not null)
        {
            TransitionTo(RecordingState.Error);
            _stopCompletion?.TrySetException(e.Exception);
            return;
        }

        TransitionTo(RecordingState.Stopped);

        if (path is not null)
        {
            _stopCompletion?.TrySetResult(new RecordingResult(path, duration));
        }
        else
        {
            _stopCompletion?.TrySetException(
                new InvalidOperationException("The output path of the recording could not be determined."));
        }
    }

    private void TransitionTo(RecordingState next)
    {
        _state = next;
        StateChanged?.Invoke(this, next);
    }

    private void DisposeNativeResources()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _writer?.Dispose();
        _writer = null;
        _stopwatch = null;
    }
}
