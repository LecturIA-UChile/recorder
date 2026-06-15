using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Generates output paths for recordings under
/// <c>%USERPROFILE%\Desktop\LecturIA_grabaciones</c>.
/// </summary>
/// <remarks>
/// Storing recordings on the user's desktop matches the expectation of the
/// original Python application and makes the files trivially discoverable
/// for non-technical users.
/// </remarks>
public sealed class DesktopRecordingPathResolver : IRecordingPathResolver
{
    private const string FolderName = "LecturIA_grabaciones";

    /// <summary>
    /// Initializes the resolver and ensures the recordings folder exists.
    /// </summary>
    public DesktopRecordingPathResolver()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        RecordingsFolder = Path.Combine(desktop, FolderName);
        Directory.CreateDirectory(RecordingsFolder);
    }

    /// <inheritdoc />
    public string RecordingsFolder { get; }

    /// <inheritdoc />
    public string ResolveFor(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        var safeName = SanitizeFileName(student.Name);
        var basePath = Path.Combine(RecordingsFolder, $"{safeName}.wav");

        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (var i = 1; i < int.MaxValue; i++)
        {
            var candidate = Path.Combine(RecordingsFolder, $"{safeName}_{i}.wav");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique path for the recording.");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(sanitized) ? "estudiante" : sanitized;
    }
}
