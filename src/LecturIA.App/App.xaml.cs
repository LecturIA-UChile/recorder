using System.Windows;

using LecturIA.App.Infrastructure;
using LecturIA.App.Services;
using LecturIA.App.ViewModels;
using LecturIA.Core.Abstractions;
using LecturIA.Infrastructure.Audio;
using LecturIA.Infrastructure.Importers;
using LecturIA.Infrastructure.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace LecturIA.App;

/// <summary>
/// WPF application entry point. Owns the dependency injection container,
/// the lifetime of the main window, and the active UI theme.
/// </summary>
public partial class App : Application
{
    private const string LightThemeSource = "Themes/Light.xaml";
    private const string DarkThemeSource  = "Themes/Dark.xaml";

    private ServiceProvider? _services;

    /// <summary>Service provider exposed for advanced scenarios.</summary>
    /// <exception cref="InvalidOperationException">
    /// The service provider has not been initialized yet (called before
    /// <see cref="OnStartup(StartupEventArgs)"/> ran).
    /// </exception>
    public IServiceProvider Services => _services
        ?? throw new InvalidOperationException("The service container has not been initialized yet.");

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // Apply the persisted theme before the window is shown so there is
        // no flash of the wrong theme on startup.
        var settings = _services.GetRequiredService<AppSettings>();
        ApplyTheme(settings.IsDarkMode);

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Swaps the active theme dictionary at runtime. Safe to call at any
    /// time after the application resources have been initialized.
    /// </summary>
    /// <param name="isDark">
    /// <see langword="true"/> to activate the dark theme;
    /// <see langword="false"/> to restore the light theme.
    /// </param>
    public void ApplyTheme(bool isDark)
    {
        var source = new Uri(
            isDark ? DarkThemeSource : LightThemeSource,
            UriKind.Relative);

        var merged = Resources.MergedDictionaries;

        // Replace the first merged dictionary (the theme slot) in-place so
        // all DynamicResource bindings across every open window update
        // automatically.
        if (merged.Count > 0)
        {
            merged[0] = new ResourceDictionary { Source = source };
        }
        else
        {
            merged.Add(new ResourceDictionary { Source = source });
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var settings = AppSettings.Load();

        services.AddSingleton(settings);
        services.AddSingleton<IRecordingPathResolver>(
            _ => new DesktopRecordingPathResolver());
        services.AddSingleton<ICourseRepository>(_ =>
            new JsonCourseRepository(AppPaths.CoursesFile));
        services.AddSingleton<ICourseImporter, CourseFileImporter>();
        services.AddSingleton<IDialogService, DialogService>();

        // The audio recorder retains state across Start/Stop, so a single
        // instance is shared for the lifetime of the application.
        services.AddSingleton<IAudioRecorder, NAudioRecorder>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
