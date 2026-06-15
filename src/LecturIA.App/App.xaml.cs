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
/// WPF application entry point. Owns the dependency injection container
/// and the lifetime of the main window.
/// </summary>
public partial class App : Application
{
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

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IRecordingPathResolver, DesktopRecordingPathResolver>();
        services.AddSingleton<IStudentRepository>(_ =>
            new JsonStudentRepository(AppPaths.StudentsFile));
        services.AddSingleton<IStudentImporter, StudentFileImporter>();
        services.AddSingleton<IDialogService, DialogService>();

        // The audio recorder retains state across Start/Stop, so a single
        // instance is shared for the lifetime of the application.
        services.AddSingleton<IAudioRecorder, NAudioRecorder>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
