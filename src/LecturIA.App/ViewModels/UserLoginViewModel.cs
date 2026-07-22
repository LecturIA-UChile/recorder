using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.App.ViewModels;

/// <summary>
/// Coordinates the provider-neutral login shown before the main window.
/// </summary>
public sealed partial class UserLoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;
    private CancellationTokenSource? _signInCancellation;

    /// <summary>Creates the login view model for the configured provider.</summary>
    public UserLoginViewModel(IAuthenticationService authenticationService)
    {
        ArgumentNullException.ThrowIfNull(authenticationService);
        _authenticationService = authenticationService;
    }

    /// <summary>Identity returned by the provider after a successful login.</summary>
    public AuthenticatedUser? AuthenticatedUser { get; private set; }

    /// <summary>Whether the configured provider can currently start a login flow.</summary>
    public bool IsAuthenticationAvailable => _authenticationService.IsAvailable;

    /// <summary>Error shown when the provider cannot establish a session.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary><see langword="true"/> while the provider login is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy;

    /// <summary>Convenience flag, opposite of <see cref="IsBusy"/>.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>Raised after the provider establishes an identity.</summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>Raised when the user chooses to exit without signing in.</summary>
    public event EventHandler? LoginCancelled;

    /// <summary>Cancels a provider login that is waiting for external completion.</summary>
    public void CancelPendingSignIn() => _signInCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        using var cancellation = new CancellationTokenSource();
        _signInCancellation = cancellation;

        try
        {
            AuthenticatedUser = await _authenticationService.SignInAsync(cancellation.Token);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "El intento se canceló. Puedes iniciar sesión nuevamente.";
        }
        catch (AuthenticationFlowException)
        {
            ErrorMessage = "No pudimos iniciar sesión. Revisa tu conexión e inténtalo nuevamente.";
        }
        catch (Exception)
        {
            ErrorMessage = "No se pudo iniciar sesión. Inténtalo nuevamente.";
        }
        finally
        {
            if (ReferenceEquals(_signInCancellation, cancellation))
            {
                _signInCancellation = null;
            }

            IsBusy = false;
        }
    }

    private bool CanSignIn() => !IsBusy && _authenticationService.IsAvailable;

    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            CancelPendingSignIn();
            return;
        }

        LoginCancelled?.Invoke(this, EventArgs.Empty);
    }
}
