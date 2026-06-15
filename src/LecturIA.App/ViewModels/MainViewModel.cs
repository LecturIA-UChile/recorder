using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LecturIA.App.Services;
using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;
using LecturIA.Core.Recording;

namespace LecturIA.App.ViewModels;

/// <summary>
/// Top-level view model that orchestrates importing, searching, selecting
/// and recording for a student.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IStudentRepository _studentRepository;
    private readonly IStudentImporter _studentImporter;
    private readonly IAudioRecorder _audioRecorder;
    private readonly IRecordingPathResolver _pathResolver;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// Creates the view model with all its required collaborators injected.
    /// </summary>
    public MainViewModel(
        IStudentRepository studentRepository,
        IStudentImporter studentImporter,
        IAudioRecorder audioRecorder,
        IRecordingPathResolver pathResolver,
        IDialogService dialogService)
    {
        _studentRepository = studentRepository;
        _studentImporter = studentImporter;
        _audioRecorder = audioRecorder;
        _pathResolver = pathResolver;
        _dialogService = dialogService;

        Students = new ObservableCollection<Student>();
        StudentsView = CollectionViewSource.GetDefaultView(Students);
        StudentsView.Filter = MatchesSearch;

        _audioRecorder.StateChanged += OnRecorderStateChanged;
        StatusMessage = "Listo. Importa una lista de estudiantes para comenzar.";
        RecordingsFolderHint = $"Las grabaciones se guardan en: {_pathResolver.RecordingsFolder}";
    }

    /// <summary>Backing collection for the student list, bound to the UI.</summary>
    public ObservableCollection<Student> Students { get; }

    /// <summary>Filterable view over <see cref="Students"/> driven by <see cref="SearchText"/>.</summary>
    public ICollectionView StudentsView { get; }

    /// <summary>Static hint shown to the user with the recordings location.</summary>
    public string RecordingsFolderHint { get; }

    /// <summary>Currently selected student, or <see langword="null"/> when none.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private Student? _selectedStudent;

    /// <summary>Free-text filter applied to <see cref="StudentsView"/>.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Status message displayed at the bottom of the window.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary><see langword="true"/> while a recording is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ImportStudentsCommand))]
    private bool _isRecording;

    /// <summary><see langword="true"/> while an asynchronous operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportStudentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private bool _isBusy;

    /// <summary>Convenience flag, opposite of <see cref="IsRecording"/>.</summary>
    public bool IsIdle => !IsRecording;

    /// <summary>Caption of the record/stop button.</summary>
    public string RecordButtonText => IsRecording ? "Detener grabación" : "Grabar voz";

    /// <summary>Loads the persisted student list when the window first appears.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var existing = await _studentRepository.LoadAsync();
            ReplaceStudents(existing);
            StatusMessage = existing.Count > 0
                ? $"{existing.Count} estudiante(s) cargado(s)."
                : "Aún no hay estudiantes. Importa una lista para comenzar.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error al cargar estudiantes", ex.Message);
        }
    }

    partial void OnSearchTextChanged(string value) => StudentsView.Refresh();

    private bool MatchesSearch(object item)
    {
        if (item is not Student student)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return student.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportStudentsAsync()
    {
        var path = _dialogService.PickStudentImportFile();
        if (path is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Importando lista...";
            var imported = await _studentImporter.ImportAsync(path);
            await _studentRepository.SaveAsync(imported);
            ReplaceStudents(imported);
            StatusMessage = $"Se importaron {imported.Count} estudiantes.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo importar la lista", ex.Message);
            StatusMessage = "Importación cancelada por error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanImport() => !IsRecording && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync();
        }
        else
        {
            StartRecording();
        }
    }

    private bool CanToggleRecording() => SelectedStudent is not null && !IsBusy;

    private void StartRecording()
    {
        if (SelectedStudent is null)
        {
            return;
        }

        try
        {
            var outputPath = _pathResolver.ResolveFor(SelectedStudent);
            _audioRecorder.Start(outputPath);
            StatusMessage = $"Grabando a {SelectedStudent.Name}...";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo iniciar la grabación", ex.Message);
            StatusMessage = "Grabación no iniciada.";
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            IsBusy = true;
            var result = await _audioRecorder.StopAsync();
            StatusMessage =
                $"Grabación guardada ({result.Duration:mm\\:ss}): {Path.GetFileName(result.FilePath)}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error al detener la grabación", ex.Message);
            StatusMessage = "La grabación falló al detenerse.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnRecorderStateChanged(object? sender, RecordingState state)
    {
        // The recorder raises events from a background thread; observable
        // properties must be mutated on the UI thread to keep WPF bindings
        // happy.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyRecorderState(state);
        }
        else
        {
            dispatcher.Invoke(() => ApplyRecorderState(state));
        }
    }

    private void ApplyRecorderState(RecordingState state)
    {
        IsRecording = state == RecordingState.Recording;
    }

    private void ReplaceStudents(IReadOnlyList<Student> students)
    {
        Students.Clear();
        foreach (var s in students)
        {
            Students.Add(s);
        }

        StudentsView.Refresh();
        if (SelectedStudent is not null && !Students.Contains(SelectedStudent))
        {
            SelectedStudent = null;
        }
    }
}
