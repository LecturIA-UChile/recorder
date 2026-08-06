using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;
using LecturIA.Core.Models;
using LecturIA.Infrastructure.Crypto;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Indexes finalized recording containers by their RUT-derived opaque identifier.
/// </summary>
/// <remarks>
/// Legacy file names are decoded in memory and immediately converted to the
/// current RUT-only identifier. Plain student data is never written by this index.
/// </remarks>
public sealed class CompletedRecordingIndex : IRecordingStatusProvider, IDisposable
{
    private const int RefreshDebounceMilliseconds = 300;

    private readonly string _recordingsFolder;
    private readonly IStudentRecordingIdProvider _recordingIdProvider;
    private readonly IStudentUidCodec _legacyUidCodec;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _refreshTimer;
    private volatile HashSet<string> _completedIds = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    /// <summary>Creates an index for the active recordings folder.</summary>
    public CompletedRecordingIndex(
        IRecordingPathResolver pathResolver,
        IStudentRecordingIdProvider recordingIdProvider,
        IStudentUidCodec legacyUidCodec)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(recordingIdProvider);
        ArgumentNullException.ThrowIfNull(legacyUidCodec);

        _recordingsFolder = pathResolver.RecordingsFolder;
        _recordingIdProvider = recordingIdProvider;
        _legacyUidCodec = legacyUidCodec;

        _refreshTimer = new System.Threading.Timer(
            _ => RefreshAfterFileSystemChange(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
        _watcher = new FileSystemWatcher(
            _recordingsFolder,
            $"*{EncryptedRecordingFormat.FileExtension}")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += OnRecordingFileChanged;
        _watcher.Changed += OnRecordingFileChanged;
        _watcher.Deleted += OnRecordingFileChanged;
        _watcher.Renamed += OnRecordingFileRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public void Refresh()
    {
        var completedIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         _recordingsFolder,
                         $"*{EncryptedRecordingFormat.FileExtension}",
                         SearchOption.TopDirectoryOnly))
            {
                if (!IsFinalizedRecording(filePath))
                {
                    continue;
                }

                var fileId = RemoveCollisionSuffix(Path.GetFileNameWithoutExtension(filePath));
                if (TryResolveCurrentRecordingId(fileId, out var recordingId))
                {
                    completedIds.Add(recordingId);
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        _completedIds = completedIds;
    }

    /// <inheritdoc />
    public bool HasCompletedRecording(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);
        return _completedIds.Contains(_recordingIdProvider.GetId(student));
    }

    /// <inheritdoc />
    public void RetainOnlyLatestRecording(Student student, string latestFilePath)
    {
        ArgumentNullException.ThrowIfNull(student);
        ArgumentException.ThrowIfNullOrWhiteSpace(latestFilePath);

        var latestFullPath = Path.GetFullPath(latestFilePath);
        var recordingsFolderFullPath = Path.GetFullPath(_recordingsFolder);
        if (!string.Equals(
                Path.GetDirectoryName(latestFullPath),
                recordingsFolderFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The latest recording path is outside the active recordings folder.",
                nameof(latestFilePath));
        }

        if (!File.Exists(latestFullPath) || !IsFinalizedRecording(latestFullPath))
        {
            throw new InvalidOperationException(
                "The latest recording file is missing or was not finalized correctly.");
        }

        var expectedRecordingId = _recordingIdProvider.GetId(student);
        var latestFileId = RemoveCollisionSuffix(Path.GetFileNameWithoutExtension(latestFullPath));
        if (!TryResolveCurrentRecordingId(latestFileId, out var latestRecordingId) ||
            !string.Equals(latestRecordingId, expectedRecordingId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The latest recording path does not belong to the supplied student.",
                nameof(latestFilePath));
        }

        var deletionFailures = new List<Exception>();
        try
        {
            foreach (var candidatePath in Directory.EnumerateFiles(
                         _recordingsFolder,
                         $"*{EncryptedRecordingFormat.FileExtension}",
                         SearchOption.TopDirectoryOnly))
            {
                var candidateFullPath = Path.GetFullPath(candidatePath);
                if (string.Equals(candidateFullPath, latestFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidateFileId = RemoveCollisionSuffix(
                    Path.GetFileNameWithoutExtension(candidateFullPath));
                if (!TryResolveCurrentRecordingId(candidateFileId, out var candidateRecordingId) ||
                    !string.Equals(candidateRecordingId, expectedRecordingId, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    File.Delete(candidateFullPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    deletionFailures.Add(ex);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Could not enumerate previous recording files.", ex);
        }
        finally
        {
            Refresh();
        }

        if (deletionFailures.Count > 0)
        {
            throw new IOException(
                "One or more previous recording files could not be deleted.",
                new AggregateException(deletionFailures));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnRecordingFileChanged;
        _watcher.Changed -= OnRecordingFileChanged;
        _watcher.Deleted -= OnRecordingFileChanged;
        _watcher.Renamed -= OnRecordingFileRenamed;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _refreshTimer.Dispose();
    }

    private void OnRecordingFileChanged(object sender, FileSystemEventArgs e) =>
        ScheduleRefresh();

    private void OnRecordingFileRenamed(object sender, RenamedEventArgs e) =>
        ScheduleRefresh();

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _refreshTimer.Change(RefreshDebounceMilliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RefreshAfterFileSystemChange()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryResolveCurrentRecordingId(string fileId, out string recordingId)
    {
        if (fileId.StartsWith(HmacStudentRecordingIdProvider.Prefix, StringComparison.Ordinal))
        {
            recordingId = fileId;
            return true;
        }

        try
        {
            var student = _legacyUidCodec.Decode(fileId);
            recordingId = _recordingIdProvider.GetId(student);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (FormatException)
        {
        }
        catch (CryptographicException)
        {
        }

        recordingId = string.Empty;
        return false;
    }

    private static string RemoveCollisionSuffix(string fileNameWithoutExtension)
    {
        var separator = fileNameWithoutExtension.LastIndexOf('_');
        if (separator <= 0)
        {
            return fileNameWithoutExtension;
        }

        var suffix = fileNameWithoutExtension.AsSpan(separator + 1);
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) &&
               sequence > 0
            ? fileNameWithoutExtension[..separator]
            : fileNameWithoutExtension;
    }

    private static bool IsFinalizedRecording(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return HasCompleteContainer(stream);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasCompleteContainer(FileStream stream)
    {
        Span<byte> magic = stackalloc byte[EncryptedRecordingFormat.MagicLength];
        if (!TryReadExactly(stream, magic) || !magic.SequenceEqual(EncryptedRecordingFormat.Magic))
        {
            return false;
        }

        var version = stream.ReadByte();
        if (version is < 1 or > EncryptedRecordingFormat.CurrentVersion || stream.ReadByte() < 0)
        {
            return false;
        }

        if (!TrySkip(stream, EncryptedRecordingFormat.FingerprintLength) ||
            !TryReadUInt32(stream, out var wrappedKeyLength) ||
            wrappedKeyLength == 0 ||
            !TrySkip(stream, wrappedKeyLength) ||
            !TrySkip(stream, EncryptedRecordingFormat.NoncePrefixLength) ||
            !TryReadUInt32(stream, out var chunkSize) ||
            chunkSize == 0 ||
            !TryReadUInt16(stream, out var mimeLength) ||
            mimeLength == 0 ||
            !TrySkip(stream, mimeLength))
        {
            return false;
        }

        if (version >= 2 &&
            (!TryReadUInt32(stream, out var metadataLength) || !TrySkip(stream, metadataLength)))
        {
            return false;
        }

        var frameCount = 0L;
        while (TryReadUInt32(stream, out var ciphertextLength))
        {
            if (ciphertextLength == 0)
            {
                return frameCount > 0 && stream.Position == stream.Length;
            }

            if (ciphertextLength > chunkSize ||
                !TrySkip(stream, (long)ciphertextLength + EncryptedRecordingFormat.GcmTagLength))
            {
                return false;
            }

            frameCount++;
            if (frameCount > EncryptedRecordingFormat.MaxFrames)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadUInt16(Stream stream, out ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        if (!TryReadExactly(stream, buffer))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        return true;
    }

    private static bool TryReadUInt32(Stream stream, out uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        if (!TryReadExactly(stream, buffer))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        return true;
    }

    private static bool TryReadExactly(Stream stream, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = stream.Read(destination[offset..]);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool TrySkip(Stream stream, long byteCount)
    {
        if (byteCount < 0 || byteCount > stream.Length - stream.Position)
        {
            return false;
        }

        stream.Seek(byteCount, SeekOrigin.Current);
        return true;
    }
}
