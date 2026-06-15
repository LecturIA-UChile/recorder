using System.Windows;

using LecturIA.App.ViewModels;

namespace LecturIA.App;

/// <summary>
/// Main window of the application. The view model is supplied through DI
/// and bound to the data context for declarative XAML binding.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// Initializes the window and triggers the asynchronous bootstrap of
    /// the view model once the visual tree is loaded.
    /// </summary>
    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
}
