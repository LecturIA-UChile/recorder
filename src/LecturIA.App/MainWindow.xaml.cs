using System.Reflection;
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
    private bool _isInitialized;

    /// <summary>
    /// Initializes the window and triggers the asynchronous bootstrap of
    /// the view model once the visual tree is loaded.
    /// </summary>
    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Title = BuildTitle();
        Loaded += async (_, _) =>
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            await _viewModel.InitializeAsync();
        };
    }

    /// <summary>
    /// Clamps the window to the current screen work area and recenters it.
    /// Without this, a default size larger than the work area (which can
    /// happen on small screens or under high DPI scaling) pushes the title
    /// bar above the top edge, leaving the window impossible to drag.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var workArea = SystemParameters.WorkArea;

        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);

        Left = Math.Max(workArea.Left, workArea.Left + ((workArea.Width - Width) / 2));
        Top = Math.Max(workArea.Top, workArea.Top + ((workArea.Height - Height) / 2));
    }

    private static string BuildTitle()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(version))
        {
            // MinVer appends the default pre-release identifier "dev" when the
            // current commit has no release tag. Show a friendlier label for
            // local development builds instead of the raw version string.
            if (version.Contains("-dev.", StringComparison.Ordinal))
            {
                return "LecturIA Recorder (Development Build)";
            }

            // Strip build metadata (everything after '+') for display.
            var withoutMetadata = version.Split('+')[0];
            return $"LecturIA Recorder v{withoutMetadata}";
        }

        return "LecturIA Recorder";
    }
}
