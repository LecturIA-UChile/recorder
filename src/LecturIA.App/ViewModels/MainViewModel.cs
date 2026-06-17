using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LecturIA.App.Infrastructure;
using LecturIA.App.Services;
using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;
using LecturIA.Core.Recording;

namespace LecturIA.App.ViewModels;

/// <summary>
/// Top-level view model that orchestrates importing courses, switching
/// between them, searching their student rosters, and recording for a
/// selected student.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ICourseRepository _courseRepository;
    private readonly ICourseImporter _courseImporter;
    private readonly IAudioRecorder _audioRecorder;
    private readonly IRecordingPathResolver _pathResolver;
    private readonly IDialogService _dialogService;
    private readonly AppSettings _settings;

    /// <summary>
    /// Creates the view model with all its required collaborators injected.
    /// </summary>
    public MainViewModel(
        ICourseRepository courseRepository,
        ICourseImporter courseImporter,
        IAudioRecorder audioRecorder,
        IRecordingPathResolver pathResolver,
        IDialogService dialogService,
        AppSettings settings)
    {
        _courseRepository = courseRepository;
        _courseImporter = courseImporter;
        _audioRecorder = audioRecorder;
        _pathResolver = pathResolver;
        _dialogService = dialogService;
        _settings = settings;

        Courses = new ObservableCollection<Course>();
        Courses.CollectionChanged += OnCoursesCollectionChanged;

        Students = new ObservableCollection<Student>();
        StudentsView = CollectionViewSource.GetDefaultView(Students);
        StudentsView.Filter = MatchesSearch;

        _audioRecorder.StateChanged += OnRecorderStateChanged;
        StatusMessage = "Listo. Importa una planilla para comenzar.";
        RecordingsFolderHint = $"Las grabaciones se guardan en: {_pathResolver.RecordingsFolder}";
        _isDarkMode = settings.IsDarkMode;
    }

    /// <summary>Courses currently loaded in the application.</summary>
    public ObservableCollection<Course> Courses { get; }

    /// <summary>Students of the <see cref="SelectedCourse"/>, bound to the list view.</summary>
    public ObservableCollection<Student> Students { get; }

    /// <summary>Filterable view over <see cref="Students"/> driven by <see cref="SearchText"/>.</summary>
    public ICollectionView StudentsView { get; }

    /// <summary>Hint showing the fixed recordings folder path.</summary>
    public string RecordingsFolderHint { get; }

    /// <summary>Currently selected course, or <see langword="null"/> when none.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    private Course? _selectedCourse;

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
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    private bool _isRecording;

    /// <summary><see langword="true"/> while an asynchronous operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    private bool _isBusy;

    /// <summary><see langword="true"/> while the initial data load is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Whether the dark UI theme is active. Toggling this property swaps
    /// the application theme immediately and persists the choice to disk.
    /// </summary>
    [ObservableProperty]
    private bool _isDarkMode;

    private const int MaxCourses = 15;

    /// <summary><see langword="true"/> when the course limit has not been reached yet.</summary>
    public bool CanAddMoreCourses => Courses.Count < MaxCourses;

    /// <summary>Label shown in the warning banner when the course limit is reached.</summary>
    public static string CourseLimitMessage => $"Límite de {MaxCourses} cursos alcanzado.";

    /// <summary>Caption of the record/stop button.</summary>
    public string RecordButtonText => IsRecording ? "Detener grabación" : "Grabar voz";

    /// <summary>Convenience flag, opposite of <see cref="IsRecording"/>.</summary>
    public bool IsIdle => !IsRecording;

    /// <summary><see langword="true"/> when at least one course is loaded.</summary>
    public bool HasAnyCourse => Courses.Count > 0;

    /// <summary><see langword="true"/> when exactly one course is loaded.</summary>
    public bool IsSingleCourse => Courses.Count == 1;

    /// <summary><see langword="true"/> when more than one course is loaded.</summary>
    public bool HasMultipleCourses => Courses.Count > 1;

    /// <summary><see langword="true"/> when the selected course has no students to show.</summary>
    public bool HasNoStudents => Students.Count == 0;

    /// <summary>Loads the persisted course list when the window first appears.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            var existing = await _courseRepository.LoadAsync();
            ReplaceCourses(existing);
            if (existing.Count == 0)
            {
                StatusMessage = "Aún no hay cursos. Importa una planilla para comenzar.";
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error al cargar los cursos", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => StudentsView.Refresh();

    partial void OnIsDarkModeChanged(bool value)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme(value);
        }

        _settings.IsDarkMode = value;
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo guardar el tema", ex.Message);
        }
    }

    partial void OnSelectedCourseChanged(Course? value)
    {
        Students.Clear();
        if (value is not null)
        {
            foreach (var student in value.Students)
            {
                Students.Add(student);
            }

            StatusMessage = $"{value.Students.Count} estudiante(s) en {value.DisplayLabel}.";
        }
        else
        {
            StatusMessage = "Aún no hay cursos. Importa una planilla para comenzar.";
        }

        SelectedStudent = null;
        StudentsView.Refresh();
        OnPropertyChanged(nameof(HasNoStudents));
    }

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

        var needle = SearchText.Trim();
        return student.Rut.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || student.FirstName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || student.LastName.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanAddCourse))]
    private async Task AddCourseAsync()
    {
        var path = _dialogService.PickStudentImportFile();
        if (path is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Importando curso...";
            var course = await _courseImporter.ImportAsync(path);

            // Check for a duplicate: same school, level and section already loaded.
            var duplicate = Courses.FirstOrDefault(c =>
                string.Equals(c.School, course.School, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Level, course.Level, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Section, course.Section, StringComparison.OrdinalIgnoreCase));

            // Build the course to persist. A new Id is always generated so
            // the overwritten course is treated as a fresh import.
            var finalCourse = Course.CreateNew(
                course.School, course.Level, course.Section, course.Students);

            if (duplicate is not null)
            {
                // Replace in-place to preserve the list order.
                var index = Courses.IndexOf(duplicate);
                var nextCourses = Courses.ToList();
                nextCourses[index] = finalCourse;

                await _courseRepository.SaveAsync(nextCourses);
                Courses[index] = finalCourse;
                SelectedCourse = finalCourse;
                StatusMessage =
                    $"Curso \"{finalCourse.DisplayLabel}\" sobreescrito con {finalCourse.Students.Count} estudiante(s).";
            }
            else
            {
                // Persist first, then mutate the in-memory collection.
                var nextCourses = Courses.Append(finalCourse).ToList();
                await _courseRepository.SaveAsync(nextCourses);
                Courses.Add(finalCourse);
                SelectedCourse = finalCourse;
                StatusMessage =
                    $"Curso \"{finalCourse.DisplayLabel}\" importado con {finalCourse.Students.Count} estudiante(s).";
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo importar el curso", ex.Message);
            StatusMessage = "Importación cancelada por error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddCourse() => !IsRecording && !IsBusy && Courses.Count < MaxCourses;

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentCourse))]
    private async Task DeleteCurrentCourseAsync()
    {
        var course = SelectedCourse;
        if (course is null)
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Eliminar curso",
            $"¿Eliminar el curso \"{course.DisplayLabel}\" y sus {course.Students.Count} estudiante(s)?"
            + Environment.NewLine + Environment.NewLine
            + "Las grabaciones ya guardadas en disco no serán afectadas.");
        if (!confirmed)
        {
            return;
        }

        try
        {
            IsBusy = true;

            // Persist the new state first so the deletion survives a crash
            // mid-operation; only then update the in-memory collection.
            var nextCourses = Courses.Where(c => !ReferenceEquals(c, course)).ToList();
            await _courseRepository.SaveAsync(nextCourses);

            Courses.Remove(course);
            SelectedCourse = Courses.FirstOrDefault();
            StatusMessage = $"Curso \"{course.DisplayLabel}\" eliminado.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo eliminar el curso", ex.Message);
            StatusMessage = "Eliminación cancelada por error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDeleteCurrentCourse() =>
        SelectedCourse is not null && !IsRecording && !IsBusy;

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

    private void OnCoursesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ObservableCollection mutations do not propagate to derived bool
        // flags automatically; raise the relevant change notifications here.
        OnPropertyChanged(nameof(HasAnyCourse));
        OnPropertyChanged(nameof(IsSingleCourse));
        OnPropertyChanged(nameof(HasMultipleCourses));
        OnPropertyChanged(nameof(CanAddMoreCourses));
        AddCourseCommand.NotifyCanExecuteChanged();
    }

    private void ReplaceCourses(IReadOnlyList<Course> courses)
    {
        Courses.Clear();
        foreach (var course in courses)
        {
            Courses.Add(course);
        }

        SelectedCourse = Courses.FirstOrDefault();
    }
}
