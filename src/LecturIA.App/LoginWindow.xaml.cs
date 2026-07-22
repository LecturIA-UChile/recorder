using System.Windows;

using LecturIA.App.ViewModels;

namespace LecturIA.App;

/// <summary>
/// Modal distributor login dialog. Shown at startup when the
/// <c>--admin</c> command-line switch is supplied. The dialog is closed
/// with <see cref="Window.DialogResult"/> set to <see langword="true"/>
/// on a successful token verification and <see langword="false"/> on
/// cancellation.
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>
    /// Creates the window, wires up the view model, and subscribes to its
    /// completion events so the dialog can close itself with the right
    /// <see cref="Window.DialogResult"/>.
    /// </summary>
    /// <param name="viewModel">View model supplied via DI.</param>
    public LoginWindow(LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();

        viewModel.LoginSucceeded += OnLoginSucceeded;
        viewModel.LoginCancelled += OnLoginCancelled;
        Closed += (_, _) =>
        {
            viewModel.LoginSucceeded -= OnLoginSucceeded;
            viewModel.LoginCancelled -= OnLoginCancelled;
        };

        Loaded += (_, _) => TokenBox.Focus();
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnLoginCancelled(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
