using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Persists user preferences for the application in a JSON file located
/// alongside the student data in <c>%LOCALAPPDATA%\LecturIA</c>.
/// </summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        AppPaths.AppDataDirectory,
        "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Default reading time limit applied when no value is persisted.</summary>
    public const int DefaultReadingTimeLimitSeconds = 60;

    /// <summary>Minimum acceptable reading time limit, in seconds.</summary>
    public const int MinReadingTimeLimitSeconds = 5;

    /// <summary>Maximum acceptable reading time limit, in seconds.</summary>
    public const int MaxReadingTimeLimitSeconds = 60 * 60;

    private int _readingTimeLimitSeconds = DefaultReadingTimeLimitSeconds;

    /// <summary>
    /// Reading time limit, in seconds. When a recording reaches this value
    /// it is stopped automatically and the on-screen clock displays an
    /// alarm. Edit this entry in <c>settings.json</c> to tune the limit
    /// per deployment.
    /// </summary>
    /// <remarks>
    /// Values outside the
    /// [<see cref="MinReadingTimeLimitSeconds"/>, <see cref="MaxReadingTimeLimitSeconds"/>]
    /// range are clamped on read to keep the UI behavior sane even when the
    /// settings file has been hand-edited to a nonsensical value.
    /// </remarks>
    public int ReadingTimeLimitSeconds
    {
        get => _readingTimeLimitSeconds;
        set => _readingTimeLimitSeconds = Math.Clamp(
            value,
            MinReadingTimeLimitSeconds,
            MaxReadingTimeLimitSeconds);
    }

    /// <summary>
    /// Display name of the audio input device selected by the user.
    /// The device index is intentionally not persisted because Windows can
    /// reorder device indexes between sessions.
    /// </summary>
    public string? AudioInputDeviceName { get; set; }

    /// <summary>
    /// Loads the settings file from disk. Returns a default instance when
    /// the file does not exist or cannot be parsed.
    /// </summary>
    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>Persists the current settings to disk.</summary>
    public void Save()
    {
        Directory.CreateDirectory(AppPaths.AppDataDirectory);
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
