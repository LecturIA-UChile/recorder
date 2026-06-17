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

    /// <summary>
    /// Whether the dark UI theme is active. Defaults to <see langword="false"/>
    /// (light theme) on first run.
    /// </summary>
    public bool IsDarkMode { get; set; }

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
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
