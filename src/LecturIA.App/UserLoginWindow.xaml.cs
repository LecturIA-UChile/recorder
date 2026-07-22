using System.Windows;

using LecturIA.App.ViewModels;

namespace LecturIA.App;

/// <summary>
/// Modal window that establishes the user session before the main window opens.
/// </summary>
public partial class UserLoginWindow : Window
{
    /// <summary>Creates the window and connects it to the login lifecycle.</summary>
    public UserLoginWindow(UserLoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();

        viewModel.LoginSucceeded += OnLoginSucceeded;
        viewModel.LoginCancelled += OnLoginCancelled;
        Closed += (_, _) =>
        {
            viewModel.CancelPendingSignIn();
            viewModel.LoginSucceeded -= OnLoginSucceeded;
            viewModel.LoginCancelled -= OnLoginCancelled;
        };
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
