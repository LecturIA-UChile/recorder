using System.Diagnostics;
using System.Security.Cryptography;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Recording;

using NAudio.Lame;
using NAudio.Wave;

namespace LecturIA.Infrastructure.Audio;

/// <summary>
/// Implements audio input testing, sample playback, and encrypted MP3 recording with NAudio.
/// </summary>
/// <remarks>
/// Recordings use mono audio at 16 kHz and 16-bit, the input shape expected
/// by downstream speech models. Unencrypted MP3 bytes never touch the file system.
/// </remarks>
public sealed class NAudioRecorder : IAudioRecorder
{
    private const int SampleRateHz = 16_000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int Mp3BitrateKbps = 64;
    private const int InputTestMaxSeconds = 15;
    private const int InputTestMaxBytes =
        SampleRateHz * Channels * (BitsPerSample / 8) * InputTestMaxSeconds;

    private readonly IRecordingEncryptor _encryptor;
    private readonly object _inputTestSync = new();
    private readonly object _sessionRecordingSync = new();

    private WaveInEvent? _waveIn;
    private WaveInEvent? _inputTest;
    private MemoryStream? _inputTestBuffer;
    private byte[]? _inputTestRecording;
    private WaveOutEvent? _inputTestOutput;
    private RawSourceWaveStream? _inputTestPlaybackStream;
    private MemoryStream? _pendingSessionRecordingBuffer;
    private byte[]? _latestSessionRecording;
    private TimeSpan _latestSessionRecordingDuration;
    private TimeSpan _latestSessionRecordingPosition;
    private WaveOutEvent? _sessionRecordingOutput;
    private Mp3FileReader? _sessionRecordingPlaybackStream;
    private LameMP3FileWriter? _writer;
    private Stream? _encryptingStream;
    private string? _outputPath;
    private Stopwatch? _stopwatch;
    private RecordingState _state = RecordingState.Idle;
    private TaskCompletionSource<RecordingResult>? _stopCompletion;

    /// <summary>
    /// Creates a recorder that encrypts every captured byte before writing it to disk.
    /// </summary>
    /// <param name="encryptor">Encryptor used to protect each recording.</param>
    public NAudioRecorder(IRecordingEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        _encryptor = encryptor;
    }

    /// <inheritdoc />
    public RecordingState State => _state;

    /// <inheritdoc />
    public TimeSpan InputTestMaximumDuration => TimeSpan.FromSeconds(InputTestMaxSeconds);

    /// <inheritdoc />
    public bool HasInputTestRecording
    {
        get
        {
            lock (_inputTestSync)
            {
                return _inputTestRecording is { Length: > 0 };
            }
        }
    }

    /// <inheritdoc />
    public bool HasLatestSessionRecording
    {
        get
        {
            lock (_sessionRecordingSync)
            {
                return _latestSessionRecording is { Length: > 0 };
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan LatestSessionRecordingDuration
    {
        get
        {
            lock (_sessionRecordingSync)
            {
                return _latestSessionRecordingDuration;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan LatestSessionRecordingPosition
    {
        get
        {
            lock (_sessionRecordingSync)
            {
                if (_sessionRecordingPlaybackStream is null)
                {
                    return _latestSessionRecordingPosition;
                }

                try
                {
                    var position = _sessionRecordingPlaybackStream.CurrentTime;
                    return _latestSessionRecordingDuration > TimeSpan.Zero &&
                           position > _latestSessionRecordingDuration
                        ? _latestSessionRecordingDuration
                        : position;
                }
                catch (ObjectDisposedException)
                {
                    return _latestSessionRecordingPosition;
                }
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<RecordingState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<float>? InputLevelChanged;

    /// <inheritdoc />
    public event EventHandler<bool>? InputTestPlaybackStateChanged;

    /// <inheritdoc />
    public event EventHandler<bool>? SessionRecordingPlaybackStateChanged;

    /// <inheritdoc />
    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var deviceCount = WaveInEvent.DeviceCount;
        var devices = new List<AudioInputDevice>(deviceCount);
        var duplicateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var deviceNumber = 0; deviceNumber < deviceCount; deviceNumber++)
        {
            var capabilities = WaveInEvent.GetCapabilities(deviceNumber);
            var productName = capabilities.ProductName;
            duplicateCounts.TryGetValue(productName, out var duplicateCount);
            duplicateCount++;
            duplicateCounts[productName] = duplicateCount;

            var displayName = duplicateCount == 1
                ? productName
                : $"{productName} ({duplicateCount})";
            devices.Add(new AudioInputDevice(deviceNumber, displayName));
        }

        return devices;
    }

    /// <inheritdoc />
    public void StartInputTest(int deviceNumber)
    {
        if (_state == RecordingState.Recording ||
            _inputTest is not null ||
            _inputTestOutput is not null ||
            _sessionRecordingOutput is not null)
        {
            throw new InvalidOperationException("Another audio operation is already active.");
        }

        ValidateDeviceNumber(deviceNumber);
        ClearInputTestRecording();

        lock (_inputTestSync)
        {
            _inputTestBuffer = new MemoryStream(InputTestMaxBytes);
        }

        _inputTest = CreateWaveInEvent(deviceNumber);
        _inputTest.DataAvailable += OnInputTestDataAvailable;
        try
        {
            _inputTest.StartRecording();
        }
        catch
        {
            DisposeInputTest();
            DiscardInputTestBuffer();
            throw;
        }
    }

    /// <inheritdoc />
    public void StopInputTest()
    {
        if (_inputTest is null)
        {
            return;
        }

        DisposeInputTest();
        lock (_inputTestSync)
        {
            _inputTestRecording = _inputTestBuffer?.ToArray();
            _inputTestBuffer?.Dispose();
            _inputTestBuffer = null;
        }

        InputLevelChanged?.Invoke(this, 0);
    }

    /// <inheritdoc />
    public void PlayInputTestRecording()
    {
        if (_state == RecordingState.Recording ||
            _inputTest is not null ||
            _inputTestOutput is not null ||
            _sessionRecordingOutput is not null)
        {
            throw new InvalidOperationException("Another audio operation is already active.");
        }

        byte[] sample;
        lock (_inputTestSync)
        {
            if (_inputTestRecording is not { Length: > 0 })
            {
                throw new InvalidOperationException("No audio input test is available for playback.");
            }

            sample = _inputTestRecording;
        }

        var playbackStateRaised = false;
        try
        {
            var sampleStream = new MemoryStream(sample, writable: false);
            _inputTestPlaybackStream = new RawSourceWaveStream(sampleStream, CreateWaveFormat());
            _inputTestOutput = new WaveOutEvent();
            _inputTestOutput.PlaybackStopped += OnInputTestPlaybackStopped;
            _inputTestOutput.Init(_inputTestPlaybackStream);
            InputTestPlaybackStateChanged?.Invoke(this, true);
            playbackStateRaised = true;
            _inputTestOutput.Play();
        }
        catch
        {
            DisposeInputTestPlayback();
            if (playbackStateRaised)
            {
                InputTestPlaybackStateChanged?.Invoke(this, false);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void StopInputTestPlayback()
    {
        _inputTestOutput?.Stop();
    }

    /// <inheritdoc />
    public void ClearInputTestRecording()
    {
        StopInputTestPlayback();
        lock (_inputTestSync)
        {
            _inputTestRecording = null;
        }
    }

    /// <inheritdoc />
    public void PlayLatestSessionRecording()
    {
        if (_state == RecordingState.Recording ||
            _inputTest is not null ||
            _inputTestOutput is not null ||
            _sessionRecordingOutput is not null)
        {
            throw new InvalidOperationException("Another audio operation is already active.");
        }

        byte[] recording;
        lock (_sessionRecordingSync)
        {
            if (_latestSessionRecording is not { Length: > 0 })
            {
                throw new InvalidOperationException(
                    "No completed session recording is available for playback.");
            }

            recording = _latestSessionRecording;
        }

        var playbackStateRaised = false;
        try
        {
            var recordingStream = new MemoryStream(recording, writable: false);
            var playbackStream = new Mp3FileReader(recordingStream);
            lock (_sessionRecordingSync)
            {
                _sessionRecordingPlaybackStream = playbackStream;
                _latestSessionRecordingPosition = TimeSpan.Zero;
                if (playbackStream.TotalTime > TimeSpan.Zero)
                {
                    _latestSessionRecordingDuration = playbackStream.TotalTime;
                }
            }

            _sessionRecordingOutput = new WaveOutEvent();
            _sessionRecordingOutput.PlaybackStopped += OnSessionRecordingPlaybackStopped;
            _sessionRecordingOutput.Init(playbackStream);
            SessionRecordingPlaybackStateChanged?.Invoke(this, true);
            playbackStateRaised = true;
            _sessionRecordingOutput.Play();
        }
        catch
        {
            DisposeSessionRecordingPlayback();
            if (playbackStateRaised)
            {
                SessionRecordingPlaybackStateChanged?.Invoke(this, false);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void StopLatestSessionRecordingPlayback()
    {
        _sessionRecordingOutput?.Stop();
    }

    /// <inheritdoc />
    public void ClearLatestSessionRecording()
    {
        StopLatestSessionRecordingPlayback();
        lock (_sessionRecordingSync)
        {
            if (_latestSessionRecording is not null)
            {
                CryptographicOperations.ZeroMemory(_latestSessionRecording);
                _latestSessionRecording = null;
            }

            _latestSessionRecordingDuration = TimeSpan.Zero;
            _latestSessionRecordingPosition = TimeSpan.Zero;
        }
    }

    /// <inheritdoc />
    public void Start(
        string outputFilePath,
        int inputDeviceNumber,
        RecordingMetadata? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        if (_state == RecordingState.Recording ||
            _inputTest is not null ||
            _inputTestOutput is not null ||
            _sessionRecordingOutput is not null)
        {
            throw new InvalidOperationException("Another audio operation is already active.");
        }

        ValidateDeviceNumber(inputDeviceNumber);

        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FileStream? fileStream = null;
        try
        {
            _outputPath = outputFilePath;
            _waveIn = CreateWaveInEvent(inputDeviceNumber);
            fileStream = new FileStream(outputFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _encryptingStream = _encryptor.WrapForWrite(fileStream, metadata);
            fileStream = null;
            _pendingSessionRecordingBuffer = new MemoryStream();
            var recordingOutput = new DuplicatingWriteStream(
                _encryptingStream,
                _pendingSessionRecordingBuffer);
            _writer = new LameMP3FileWriter(
                recordingOutput,
                _waveIn.WaveFormat,
                Mp3BitrateKbps);

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _stopwatch = Stopwatch.StartNew();
            _waveIn.StartRecording();
            TransitionTo(RecordingState.Recording);
        }
        catch
        {
            CleanupFailedStart(fileStream, outputFilePath);
            throw;
        }
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
        StopInputTest();
        DisposeInputTestPlayback();
        DisposeSessionRecordingPlayback();
        DiscardInputTestBuffer();

        if (_state == RecordingState.Recording)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // Disposal must not mask the failure that initiated shutdown.
            }
        }

        DiscardPendingSessionRecording();
        lock (_inputTestSync)
        {
            _inputTestRecording = null;
        }

        ClearLatestSessionRecording();

        DisposeNativeResources();
    }

    private static WaveFormat CreateWaveFormat() =>
        new(SampleRateHz, BitsPerSample, Channels);

    private static WaveInEvent CreateWaveInEvent(int deviceNumber) => new()
    {
        DeviceNumber = deviceNumber,
        WaveFormat = CreateWaveFormat(),
        BufferMilliseconds = 50,
    };

    private static void ValidateDeviceNumber(int deviceNumber)
    {
        if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceNumber),
                deviceNumber,
                "The selected audio input device is not available.");
        }
    }

    private void OnInputTestDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_inputTestSync)
        {
            if (_inputTestBuffer is not null)
            {
                var availableBytes = InputTestMaxBytes - (int)_inputTestBuffer.Length;
                var bytesToKeep = Math.Min(e.BytesRecorded, availableBytes);
                if (bytesToKeep > 0)
                {
                    _inputTestBuffer.Write(e.Buffer, 0, bytesToKeep);
                }
            }
        }

        var peak = 0;
        for (var offset = 0; offset + 1 < e.BytesRecorded; offset += sizeof(short))
        {
            var sample = Math.Abs((int)BitConverter.ToInt16(e.Buffer, offset));
            peak = Math.Max(peak, sample);
        }

        InputLevelChanged?.Invoke(this, peak / (float)short.MaxValue);
    }

    private void OnInputTestPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        DisposeInputTestPlayback();
        InputTestPlaybackStateChanged?.Invoke(this, false);
    }

    private void OnSessionRecordingPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        CaptureSessionRecordingPlaybackPosition();
        DisposeSessionRecordingPlayback();
        SessionRecordingPlaybackStateChanged?.Invoke(this, false);
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
        var failure = e.Exception;

        try
        {
            DisposeNativeResources();
        }
        catch (Exception ex)
        {
            failure ??= ex;
            DisposeNativeResourcesIgnoringErrors();
        }

        _outputPath = null;
        if (failure is not null)
        {
            DiscardPendingSessionRecording();
            TryDeleteFailedOutput(path);
            TransitionTo(RecordingState.Error);
            _stopCompletion?.TrySetException(failure);
            return;
        }

        if (path is null)
        {
            DiscardPendingSessionRecording();
            var missingPath = new InvalidOperationException(
                "The output path of the recording could not be determined.");
            TransitionTo(RecordingState.Error);
            _stopCompletion?.TrySetException(missingPath);
            return;
        }

        PromotePendingSessionRecording(duration);
        TransitionTo(RecordingState.Stopped);
        _stopCompletion?.TrySetResult(new RecordingResult(path, duration));
    }

    private void TransitionTo(RecordingState next)
    {
        _state = next;
        StateChanged?.Invoke(this, next);
    }

    private void DisposeInputTest()
    {
        if (_inputTest is null)
        {
            return;
        }

        _inputTest.DataAvailable -= OnInputTestDataAvailable;
        _inputTest.Dispose();
        _inputTest = null;
    }

    private void DiscardInputTestBuffer()
    {
        lock (_inputTestSync)
        {
            _inputTestBuffer?.Dispose();
            _inputTestBuffer = null;
        }
    }

    private void DisposeInputTestPlayback()
    {
        if (_inputTestOutput is not null)
        {
            _inputTestOutput.PlaybackStopped -= OnInputTestPlaybackStopped;
            _inputTestOutput.Dispose();
            _inputTestOutput = null;
        }

        _inputTestPlaybackStream?.Dispose();
        _inputTestPlaybackStream = null;
    }

    private void DisposeSessionRecordingPlayback()
    {
        if (_sessionRecordingOutput is not null)
        {
            _sessionRecordingOutput.PlaybackStopped -= OnSessionRecordingPlaybackStopped;
            _sessionRecordingOutput.Dispose();
            _sessionRecordingOutput = null;
        }

        lock (_sessionRecordingSync)
        {
            _sessionRecordingPlaybackStream?.Dispose();
            _sessionRecordingPlaybackStream = null;
        }
    }

    private void CaptureSessionRecordingPlaybackPosition()
    {
        lock (_sessionRecordingSync)
        {
            if (_sessionRecordingPlaybackStream is null)
            {
                return;
            }

            try
            {
                var position = _sessionRecordingPlaybackStream.CurrentTime;
                _latestSessionRecordingPosition =
                    _latestSessionRecordingDuration > TimeSpan.Zero &&
                    position > _latestSessionRecordingDuration
                        ? _latestSessionRecordingDuration
                        : position;
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void DiscardPendingSessionRecording()
    {
        if (_pendingSessionRecordingBuffer is null)
        {
            return;
        }

        if (_pendingSessionRecordingBuffer.TryGetBuffer(out var buffer))
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan());
        }

        _pendingSessionRecordingBuffer.Dispose();
        _pendingSessionRecordingBuffer = null;
    }

    private void PromotePendingSessionRecording(TimeSpan duration)
    {
        var pendingRecording = _pendingSessionRecordingBuffer
            ?? throw new InvalidOperationException(
                "The completed recording has no in-memory playback buffer.");
        _pendingSessionRecordingBuffer = null;

        byte[] recording;
        try
        {
            recording = pendingRecording.ToArray();
        }
        finally
        {
            if (pendingRecording.TryGetBuffer(out var pendingBuffer))
            {
                CryptographicOperations.ZeroMemory(pendingBuffer.AsSpan());
            }

            pendingRecording.Dispose();
        }

        lock (_sessionRecordingSync)
        {
            if (_latestSessionRecording is not null)
            {
                CryptographicOperations.ZeroMemory(_latestSessionRecording);
            }

            _latestSessionRecording = recording;
            _latestSessionRecordingDuration = duration;
            _latestSessionRecordingPosition = TimeSpan.Zero;
        }
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
        _encryptingStream?.Dispose();
        _encryptingStream = null;
        _stopwatch = null;
    }

    private void CleanupFailedStart(FileStream? fileStream, string outputFilePath)
    {
        // Cleanup must preserve the original device or encoder failure.
        DisposeNativeResourcesIgnoringErrors(fileStream);
        DiscardPendingSessionRecording();
        _outputPath = null;
        TryDeleteFailedOutput(outputFilePath);
    }

    private void DisposeNativeResourcesIgnoringErrors(IDisposable? additionalResource = null)
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
        }

        TryDispose(_writer);
        _writer = null;
        TryDispose(_encryptingStream);
        _encryptingStream = null;
        TryDispose(additionalResource);
        TryDispose(_waveIn);
        _waveIn = null;
        _stopwatch?.Stop();
        _stopwatch = null;
    }

    private static void TryDispose(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static void TryDeleteFailedOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
