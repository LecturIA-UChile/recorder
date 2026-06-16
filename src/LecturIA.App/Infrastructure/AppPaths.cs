using System.IO;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Standard locations for user data of the application.
/// </summary>
/// <remarks>
/// User data lives under <c>%LOCALAPPDATA%\LecturIA</c>. That folder is
/// always writable for the current user, even when the application has
/// been installed under a protected location such as <c>Program Files</c>.
/// </remarks>
internal static class AppPaths
{
    /// <summary>Root folder for per-user application data.</summary>
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LecturIA");

    /// <summary>
    /// JSON file holding the persisted course list (with their students).
    /// The legacy file name <c>names.json</c> is preserved so that data
    /// stored by previous releases is still found and migrated on first
    /// load. The gitignore patterns target this exact file name.
    /// </summary>
    public static string CoursesFile { get; } = Path.Combine(AppDataDirectory, "names.json");

    /// <summary>Creates the user data directory if it does not exist.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }
}
