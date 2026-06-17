using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Generates output paths for recordings under a configurable folder,
/// defaulting to <c>%USERPROFILE%\Desktop\Grabaciones LecturIA</c>.
/// </summary>
/// <remarks>
/// Storing recordings on the user's desktop matches the expectation of the
/// original Python application and makes the files trivially discoverable
/// for non-technical users. The folder can be overridden via the
/// <paramref name="recordingsFolder"/> constructor parameter so the user can
/// choose a custom location.
/// </remarks>
public sealed class DesktopRecordingPathResolver : IRecordingPathResolver
{
    private const string DefaultFolderName = "Grabaciones LecturIA";
    private const string RecordingExtension = ".mp3";

    /// <summary>
    /// Initializes the resolver and ensures the recordings folder exists.
    /// </summary>
    public DesktopRecordingPathResolver()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        RecordingsFolder = Path.Combine(desktop, DefaultFolderName);
        Directory.CreateDirectory(RecordingsFolder);
    }

    /// <inheritdoc />
    public string RecordingsFolder { get; private set; }

    /// <inheritdoc />
    public string ResolveFor(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        var safeName = BuildFileName(student);
        var basePath = Path.Combine(RecordingsFolder, $"{safeName}{RecordingExtension}");

        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (var i = 1; i < int.MaxValue; i++)
        {
            var candidate = Path.Combine(RecordingsFolder, $"{safeName}_{i}{RecordingExtension}");
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

    private static string BuildFileName(Student student)
    {
        // Produces RUT_NOMBRE_APELLIDO. Each segment is sanitized individually
        // so that internal spaces become underscores before the segments are
        // joined, keeping the separating underscore unambiguous.
        var rut = SanitizeSegment(student.Rut);
        var firstName = SanitizeSegment(student.FirstName);
        var lastName = SanitizeSegment(student.LastName);

        var parts = new[] { rut, firstName, lastName }
            .Where(static p => !string.IsNullOrEmpty(p));

        var result = string.Join("_", parts);
        return string.IsNullOrEmpty(result) ? "SIN_IDENTIFICACION" : result;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(
            value.Trim().Select(c => c == ' ' || invalid.Contains(c) ? '_' : c).ToArray());
    }
}
