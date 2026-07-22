using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Generates output paths for recordings under
/// <c>%USERPROFILE%\Desktop\Grabaciones LecturIA</c>. File names are
/// opaque UIDs derived from the student metadata via
/// <see cref="IStudentUidCodec"/>; no plain RUT or name is written to
/// the file system.
/// </summary>
public sealed class DesktopRecordingPathResolver : IRecordingPathResolver
{
    private const string DefaultFolderName = "Grabaciones LecturIA";

    // Custom extension so the operating system does not associate the
    // files with a default media player. Recordings are encrypted blobs,
    // not playable audio.
    private const string RecordingExtension = ".lra";

    private readonly IStudentUidCodec _uidCodec;

    /// <summary>
    /// Initializes the resolver, ensures the recordings folder exists,
    /// and stores the codec used to produce UIDs.
    /// </summary>
    public DesktopRecordingPathResolver(IStudentUidCodec uidCodec)
    {
        ArgumentNullException.ThrowIfNull(uidCodec);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        RecordingsFolder = Path.Combine(desktop, DefaultFolderName);
        Directory.CreateDirectory(RecordingsFolder);

        _uidCodec = uidCodec;
    }

    /// <inheritdoc />
    public string RecordingsFolder { get; private set; }

    /// <inheritdoc />
    public string ResolveFor(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        var uid = _uidCodec.Encode(student);
        var basePath = Path.Combine(RecordingsFolder, $"{uid}{RecordingExtension}");

        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (var i = 1; i < int.MaxValue; i++)
        {
            var candidate = Path.Combine(RecordingsFolder, $"{uid}_{i}{RecordingExtension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique path for the recording.");
    }
}
