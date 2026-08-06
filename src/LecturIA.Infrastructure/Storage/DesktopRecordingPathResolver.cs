using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Generates output paths for recordings under
/// <c>%USERPROFILE%\Desktop\Grabaciones LecturIA</c>. File names use a
/// keyed pseudonym derived only from the student's RUT, so the stable
/// primary key is represented without exposing plain student data.
/// </summary>
public sealed class DesktopRecordingPathResolver : IRecordingPathResolver
{
    private const string DefaultFolderName = "Grabaciones LecturIA";

    private readonly IStudentRecordingIdProvider _recordingIdProvider;

    /// <summary>
    /// Initializes the resolver, ensures the recordings folder exists,
    /// and stores the provider used to produce RUT-based identifiers.
    /// </summary>
    public DesktopRecordingPathResolver(IStudentRecordingIdProvider recordingIdProvider)
    {
        ArgumentNullException.ThrowIfNull(recordingIdProvider);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        RecordingsFolder = Path.Combine(desktop, DefaultFolderName);
        Directory.CreateDirectory(RecordingsFolder);

        _recordingIdProvider = recordingIdProvider;
    }

    /// <inheritdoc />
    public string RecordingsFolder { get; }

    /// <inheritdoc />
    public string ResolveFor(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        var recordingId = _recordingIdProvider.GetId(student);
        var basePath = Path.Combine(
            RecordingsFolder,
            $"{recordingId}{EncryptedRecordingFormat.FileExtension}");

        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (var sequence = 1; sequence < int.MaxValue; sequence++)
        {
            var candidate = Path.Combine(
                RecordingsFolder,
                $"{recordingId}_{sequence}{EncryptedRecordingFormat.FileExtension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique path for the recording.");
    }
}
