using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LecturIA.Core.Abstractions;

namespace LecturIA.App.ViewModels;

/// <summary>
/// View model for the distributor login dialog shown when the application
/// is launched with the <c>--admin</c> command-line switch.
/// </summary>
/// <remarks>
/// The dialog blocks the main window until the user either pastes a valid
/// admin token or cancels. On success, the hosting code is expected to
/// flip <see cref="MainViewModel.IsAdminMode"/> to <see langword="true"/>.
/// On cancellation, the application opens in regular teacher mode, as if
/// the switch had not been supplied.
/// </remarks>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAdminTokenVerifier _verifier;

    /// <summary>
    /// Builds the view model wrapping the supplied admin token verifier.
    /// </summary>
    /// <param name="verifier">Service that validates tokens against the
    /// embedded public key.</param>
    public LoginViewModel(IAdminTokenVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        _verifier = verifier;
    }

    /// <summary>Token text pasted by the distributor.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _token = string.Empty;

    /// <summary>Error message displayed below the token field, when any.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// <see langword="true"/> while the verification backoff is active.
    /// Used to disable the buttons and freeze the UI briefly after a
    /// failed attempt, so repeated automated submissions cannot fly
    /// through at full speed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>Convenience flag, opposite of <see cref="IsBusy"/>.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>
    /// Raised when the supplied token verifies successfully. The hosting
    /// window listens to this event to close itself with a positive
    /// dialog result.
    /// </summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>
    /// Raised when the user explicitly cancels the login. The hosting
    /// window listens to this event to close itself with a negative
    /// dialog result.
    /// </summary>
    public event EventHandler? LoginCancelled;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            if (_verifier.Verify(Token))
            {
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
                return;
            }

            ErrorMessage = "Token inválido. Verifica que lo copiaste completo.";
            // Constant backoff slows down automated retries even though
            // the RSA verification itself is already fast.
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanVerify() => !IsBusy && !string.IsNullOrWhiteSpace(Token);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => LoginCancelled?.Invoke(this, EventArgs.Empty);

    private bool CanCancel() => !IsBusy;
}
