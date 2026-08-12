using System.Net.Http;
using System.Windows;

using LecturIA.App.Infrastructure;
using LecturIA.App.Services;
using LecturIA.App.ViewModels;
using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;
using LecturIA.Infrastructure.Audio;
using LecturIA.Infrastructure.Courses;
using LecturIA.Infrastructure.Crypto;
using LecturIA.Infrastructure.Importers;
using LecturIA.Infrastructure.Reading;
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
    private bool _adminModeRequested;
    private bool _isSigningOut;

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

        var authenticationService = _services.GetRequiredService<IAuthenticationService>();
        authenticationService.SessionExpired += OnAuthenticationSessionExpired;
        authenticationService.UserChanged += OnAuthenticatedUserChanged;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _adminModeRequested = HasAdminFlag(e.Args);

        if (!TryAuthenticateUser(out var authenticatedUser))
        {
            Shutdown();
            return;
        }

        var viewModel = _services.GetRequiredService<MainViewModel>();
        viewModel.SetAuthenticatedUser(authenticatedUser!);
        viewModel.IsAdminMode = _adminModeRequested && AuthenticateDistributor();
        viewModel.LogoutRequested += OnLogoutRequested;

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        window.Show();
    }

    private bool TryAuthenticateUser(out AuthenticatedUser? authenticatedUser)
    {
        var loginViewModel = _services!.GetRequiredService<UserLoginViewModel>();
        var login = new UserLoginWindow(loginViewModel);
        var succeeded = login.ShowDialog() == true;
        authenticatedUser = succeeded ? loginViewModel.AuthenticatedUser : null;
        return authenticatedUser is not null;
    }

    private bool AuthenticateDistributor()
    {
        var loginViewModel = _services!.GetRequiredService<LoginViewModel>();
        var login = new LoginWindow(loginViewModel);
        return login.ShowDialog() == true;
    }

    private async void OnLogoutRequested(object? sender, EventArgs e) =>
        await ReauthenticateAsync();

    private void OnAuthenticationSessionExpired(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => _ = HandleExpiredSessionAsync()));
    }

    private void OnAuthenticatedUserChanged(object? sender, AuthenticatedUserChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _services?.GetRequiredService<MainViewModel>().SetAuthenticatedUser(e.User);
        }));
    }

    private async Task HandleExpiredSessionAsync()
    {
        var services = _services;
        if (services is null)
        {
            return;
        }

        var viewModel = services.GetRequiredService<MainViewModel>();
        while (!viewModel.CanEndSession)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        services.GetRequiredService<IDialogService>().ShowInfo(
            "Sesión expirada",
            "Tu sesión de LecturIA expiró y debes iniciar sesión nuevamente.");
        await ReauthenticateAsync();
    }

    private async Task ReauthenticateAsync()
    {
        if (_isSigningOut || MainWindow is not MainWindow window)
        {
            return;
        }

        _isSigningOut = true;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Hide();

        var services = _services
            ?? throw new InvalidOperationException("The service container has not been initialized yet.");
        var viewModel = services.GetRequiredService<MainViewModel>();
        try
        {
            string? logoutWarning = null;
            try
            {
                await services.GetRequiredService<IAuthenticationService>().SignOutAsync();
            }
            catch (AuthenticationFlowException)
            {
                logoutWarning = "La sesión se cerró en este equipo, pero no pudimos completar el proceso. Puedes volver a intentarlo.";
            }
            catch (Exception)
            {
                logoutWarning = "La sesión se cerró en este equipo, pero no pudimos completar el proceso. Puedes volver a intentarlo.";
            }

            viewModel.ClearAuthenticatedUser();
            viewModel.IsAdminMode = false;

            if (logoutWarning is not null)
            {
                services.GetRequiredService<IDialogService>().ShowInfo(
                    "Cierre de sesión incompleto",
                    logoutWarning);
            }

            if (!TryAuthenticateUser(out var authenticatedUser))
            {
                Shutdown();
                return;
            }

            viewModel.SetAuthenticatedUser(authenticatedUser!);
            viewModel.IsAdminMode = _adminModeRequested && AuthenticateDistributor();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            window.Show();

            // The window (and its view model) are reused across sessions, so
            // the Loaded bootstrap does not run again. Reload the roster for
            // the newly signed-in teacher explicitly.
            await viewModel.ReloadCoursesAsync();
        }
        catch (Exception)
        {
            services.GetRequiredService<IDialogService>().ShowError(
                "No se pudo cambiar de usuario",
                "No fue posible iniciar una nueva sesión. LecturIA se cerrará.");
            Shutdown();
        }
        finally
        {
            _isSigningOut = false;
        }
    }

    private static bool HasAdminFlag(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            var authenticationService = _services.GetRequiredService<IAuthenticationService>();
            authenticationService.SessionExpired -= OnAuthenticationSessionExpired;
            authenticationService.UserChanged -= OnAuthenticatedUserChanged;
        }

        if (_services is not null)
        {
            try
            {
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }

            _services = null;
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var settings = AppSettings.Load();

        services.AddSingleton(settings);
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        services.AddSingleton<CognitoOptions>();
        services.AddSingleton<CognitoTokenValidator>();
        services.AddSingleton<IAuthenticationService, CognitoAuthenticationService>();

        // Courses are fetched from the control plane with the Cognito access
        // token. The client owns a dedicated HttpClient (its own base address
        // and 35s timeout for database cold starts), kept separate from the
        // shared client used by the authentication flow.
        services.AddSingleton<ICoursesClient>(_ => new HttpCoursesClient());

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ICourseImporter, CourseFileImporter>();

        // Pseudonymization pipeline. The UID key is loaded once from the
        // embedded resource and used to derive the SIV-style codec used
        // by both the path resolver and the course repository, so PII
        // never leaves the in-memory model.
        services.AddSingleton<IStudentUidKeyProvider>(_ =>
            new InMemoryStudentUidKeyProvider(EmbeddedUidKeyResource.Load()));
        services.AddSingleton<IStudentUidCodec>(sp =>
            new SivStudentUidCodec(sp.GetRequiredService<IStudentUidKeyProvider>()));
        services.AddSingleton<IStudentRecordingIdProvider>(sp =>
            new HmacStudentRecordingIdProvider(
                sp.GetRequiredService<IStudentUidKeyProvider>()));
        services.AddSingleton<ICourseUidCodec>(sp =>
            new SivCourseUidCodec(sp.GetRequiredService<IStudentUidKeyProvider>()));

        services.AddSingleton<IRecordingPathResolver>(sp =>
            new DesktopRecordingPathResolver(
                sp.GetRequiredService<IStudentRecordingIdProvider>()));
        services.AddSingleton<IRecordingStatusProvider>(sp =>
            new CompletedRecordingIndex(
                sp.GetRequiredService<IRecordingPathResolver>(),
                sp.GetRequiredService<IStudentRecordingIdProvider>(),
                sp.GetRequiredService<IStudentUidCodec>()));
        services.AddSingleton<ICourseRepository>(sp =>
            new JsonCourseRepository(
                AppPaths.CoursesFile,
                sp.GetRequiredService<IStudentUidCodec>(),
                sp.GetRequiredService<ICourseUidCodec>(),
                AppPaths.LegacyNamesFile));

        // Recording encryption. The public key is loaded once from the
        // embedded PEM and reused for every recording. The private key is
        // not present in this binary by design; only the application
        // owners can decrypt files produced here.
        services.AddSingleton<IPublicKeyProvider>(_ =>
            new PemPublicKeyProvider(EmbeddedPublicKeyResource.Load()));
        services.AddSingleton<IRecordingEncryptor>(sp =>
            new HybridRecordingEncryptor(sp.GetRequiredService<IPublicKeyProvider>()));

        // The audio recorder retains state across Start/Stop, so a single
        // instance is shared for the lifetime of the application.
        services.AddSingleton<IAudioRecorder, NAudioRecorder>();

        // Admin token verification uses a dedicated set of Ed25519
        // distributor public keys, separate from the RSA recording key.
        // A valid token is a base64 Ed25519 signature over the fixed
        // admin payload, produced offline by a distributor with their
        // private key. The token is accepted if it verifies against any
        // embedded public key, which supports multiple distributors.
        services.AddSingleton<IAdminTokenVerifier>(_ =>
            new Ed25519AdminTokenVerifier(
                AdminPublicKeySet.Parse(EmbeddedAdminPublicKeysResource.Load())));

        // Reading passages are a fixed set embedded in the binary and
        // parsed once at startup. They are shown on screen for students
        // to read aloud while recording.
        services.AddSingleton<IReadingTextProvider>(_ =>
            new JsonReadingTextProvider(EmbeddedReadingTextsResource.Load()));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<UserLoginViewModel>();
        services.AddTransient<LoginViewModel>();
    }
}
