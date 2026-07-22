using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;

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
    private readonly DispatcherTimer _readingTimer;
    private readonly DispatcherTimer _audioTestTimer;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);
    private const int CountdownSeconds = 3;
    private static readonly TimeSpan CountdownStepInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CountdownCueInterval = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Creates the view model with all its required collaborators injected.
    /// </summary>
    public MainViewModel(
        ICourseRepository courseRepository,
        ICourseImporter courseImporter,
        IAudioRecorder audioRecorder,
        IRecordingPathResolver pathResolver,
        IDialogService dialogService,
        IReadingTextProvider readingTextProvider,
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

        ReadingTexts = new ObservableCollection<ReadingText>(readingTextProvider.GetAll());
        ReadingTextsView = CollectionViewSource.GetDefaultView(ReadingTexts);
        ReadingTextsView.Filter = MatchesReadingLevel;

        AudioInputDevices = new ObservableCollection<AudioInputDevice>();
        _audioRecorder.StateChanged += OnRecorderStateChanged;
        _audioRecorder.InputLevelChanged += OnInputLevelChanged;
        _audioRecorder.InputTestPlaybackStateChanged += OnInputTestPlaybackStateChanged;
        StatusMessage = "Listo.";
        RecordingsFolderHint = "Las grabaciones se guardan en tu escritorio bajo la carpeta \"Grabaciones LecturIA\".";

        _readingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TickInterval,
        };
        _readingTimer.Tick += OnReadingTimerTick;

        _audioTestTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = _audioRecorder.InputTestMaximumDuration,
        };
        _audioTestTimer.Tick += OnAudioTestTimerTick;
    }

    /// <summary>Courses currently loaded in the application.</summary>
    public ObservableCollection<Course> Courses { get; }

    /// <summary>Students of the <see cref="SelectedCourse"/>, bound to the list view.</summary>
    public ObservableCollection<Student> Students { get; }

    /// <summary>The fixed set of reading passages available for recording selection.</summary>
    public ObservableCollection<ReadingText> ReadingTexts { get; }

    /// <summary>Audio input devices currently reported by Windows.</summary>
    public ObservableCollection<AudioInputDevice> AudioInputDevices { get; }

    /// <summary>The input device used for tests and recordings.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyPropertyChangedFor(nameof(RecordButtonBlockedReason))]
    private AudioInputDevice? _selectedAudioInput;

    /// <summary><see langword="true"/> while the selected input device is being tested.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioTestButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    private bool _isTestingAudio;

    /// <summary>Current input peak as a percentage from 0 to 100.</summary>
    [ObservableProperty]
    private double _audioInputLevel;

    /// <summary>Whether the most recent microphone test has an audible sample.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    private bool _hasAudioTestRecording;

    /// <summary><see langword="true"/> while the microphone test sample is playing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioPlaybackButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    private bool _isPlayingAudioTest;

    /// <summary>Caption of the input test button.</summary>
    public string AudioTestButtonText => IsTestingAudio ? "Detener" : "Probar";

    /// <summary>Caption of the test-sample playback button.</summary>
    public string AudioPlaybackButtonText => IsPlayingAudioTest ? "Detener" : "Escuchar";

    /// <summary>
    /// View over <see cref="ReadingTexts"/> filtered to the level of the
    /// <see cref="SelectedCourse"/>, so the teacher only sees passages that
    /// match the grade they are recording.
    /// </summary>
    public ICollectionView ReadingTextsView { get; }

    /// <summary>
    /// Level (1 or 2) inferred from the selected course, or
    /// <see langword="null"/> when it could not be determined. When null,
    /// no level filter is applied and every text is shown.
    /// </summary>
    private int? _activeReadingLevel;

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
    [NotifyPropertyChangedFor(nameof(RecordButtonBlockedReason))]
    private Student? _selectedStudent;

    /// <summary>Free-text filter applied to <see cref="StudentsView"/>.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Reading passage selected for recording metadata, or <see langword="null"/>
    /// when the teacher has not picked one yet.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonBlockedReason))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private ReadingText? _selectedReadingText;

    /// <summary>Status message displayed at the bottom of the window.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary><see langword="true"/> while a recording is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    [NotifyPropertyChangedFor(nameof(RecordButtonBlockedReason))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    private bool _isRecording;

    /// <summary><see langword="true"/> while an asynchronous operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    private bool _isBusy;

    /// <summary><see langword="true"/> while the initial data load is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    private bool _isLoading;

    /// <summary>
    /// <see langword="true"/> while the pre-recording countdown overlay is
    /// shown. The record button is disabled during this window so a second
    /// recording cannot be started mid-countdown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    private bool _isCountingDown;

    /// <summary>
    /// Text displayed in the countdown overlay, cycling through the
    /// remaining seconds and finishing with the "start now" cue.
    /// </summary>
    [ObservableProperty]
    private string _countdownText = string.Empty;

    /// <summary>
    /// Whether the application is running in distributor (admin) mode.
    /// When <see langword="true"/> the distributor authorization contributes
    /// to <see cref="CanManageCourses"/>.
    /// </summary>
    /// <remarks>
    /// Set once at startup from <see cref="App"/> based on the command
    /// line. The same compiled binary is shipped to every teacher; the
    /// data installed in <c>%LOCALAPPDATA%\LecturIA</c> is what changes
    /// per deployment.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageCourses))]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    private bool _isAdminMode;

    /// <summary>Whether the current distributor authorization or Cognito role permits course management.</summary>
    public bool CanManageCourses =>
        IsAdminMode ||
        AuthenticatedUser?.Role is UserRole.Maintainer or UserRole.Administrator;

    /// <summary>Complete identity established for the current session.</summary>
    public AuthenticatedUser? AuthenticatedUser { get; private set; }

    /// <summary>Name displayed for the identity associated with the current session.</summary>
    [ObservableProperty]
    private string _authenticatedDisplayName = string.Empty;

    /// <summary>Localized role name associated with the current provider session.</summary>
    [ObservableProperty]
    private string _authenticatedRole = string.Empty;

    /// <summary>Raised when the user requests that the current session be ended.</summary>
    public event EventHandler? LogoutRequested;

    private const int MaxCourses = 15;

    /// <summary><see langword="true"/> when the course limit has not been reached yet.</summary>
    public bool CanAddMoreCourses => Courses.Count < MaxCourses;

    /// <summary>Label shown in the warning banner when the course limit is reached.</summary>
    public static string CourseLimitMessage => $"Límite de {MaxCourses} cursos alcanzado.";

    /// <summary>Caption of the record/stop button.</summary>
    public string RecordButtonText => IsRecording
        ? "Detener grabación"
        : "Comenzar grabación";

    /// <summary>
    /// Tooltip shown on the record button when it is disabled, explaining
    /// what the teacher still needs to do before recording can start.
    /// Returns <see langword="null"/> when the button is enabled (no
    /// tooltip needed).
    /// </summary>
    public string? RecordButtonBlockedReason
    {
        get
        {
            if (IsRecording || IsBusy || IsCountingDown)
            {
                return null;
            }

            if (SelectedAudioInput is null)
            {
                return "Conecta o selecciona un micrófono antes de comenzar la grabación.";
            }

            var missingStudent = SelectedStudent is null;
            var missingText = SelectedReadingText is null;

            if (missingStudent && missingText)
            {
                return "Selecciona un estudiante y una lectura antes de comenzar la grabación.";
            }

            if (missingStudent)
            {
                return "Selecciona un estudiante antes de comenzar la grabación.";
            }

            if (missingText)
            {
                return "Selecciona una lectura antes de comenzar la grabación.";
            }

            return null;
        }
    }

    /// <summary>Configured reading time limit, in seconds.</summary>
    public int ReadingTimeLimitSeconds => _settings.ReadingTimeLimitSeconds;

    /// <summary>Wall-clock elapsed time for the current (or most recent) recording.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedTimeText))]
    private TimeSpan _elapsedTime = TimeSpan.Zero;

    /// <summary>
    /// <see langword="true"/> once the reading time limit has been reached.
    /// Drives the on-screen alarm animation and disables further ticks
    /// until the next recording starts.
    /// </summary>
    [ObservableProperty]
    private bool _isTimeUp;

    /// <summary>Elapsed time formatted as <c>mm:ss</c>.</summary>
    public string ElapsedTimeText =>
        $"{(int)ElapsedTime.TotalMinutes:D2}:{ElapsedTime.Seconds:D2}";

    /// <summary>Convenience flag, opposite of <see cref="IsRecording"/>.</summary>
    public bool IsIdle => !IsRecording && !IsCountingDown;

    /// <summary><see langword="true"/> when at least one course is loaded.</summary>
    public bool HasAnyCourse => Courses.Count > 0;

    /// <summary><see langword="true"/> when exactly one course is loaded.</summary>
    public bool IsSingleCourse => Courses.Count == 1;

    /// <summary><see langword="true"/> when more than one course is loaded.</summary>
    public bool HasMultipleCourses => Courses.Count > 1;

    /// <summary><see langword="true"/> when the selected course has no students to show.</summary>
    public bool HasNoStudents => Students.Count == 0;

    /// <summary>Assigns the identity established for the active session.</summary>
    public void SetAuthenticatedUser(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        AuthenticatedUser = user;
        AuthenticatedDisplayName = user.DisplayName;
        AuthenticatedRole = user.Role switch
        {
            UserRole.Professor => "Profesor",
            UserRole.Director => "Director",
            UserRole.Maintainer => "Mantenedor",
            UserRole.Administrator => "Admin",
            _ => throw new ArgumentOutOfRangeException(nameof(user), user.Role, "Unknown user role."),
        };
        NotifyCourseManagementPermissionChanged();
    }

    /// <summary>Removes the identity associated with the previous session.</summary>
    public void ClearAuthenticatedUser()
    {
        AuthenticatedUser = null;
        AuthenticatedDisplayName = string.Empty;
        AuthenticatedRole = string.Empty;
        NotifyCourseManagementPermissionChanged();
    }

    private void NotifyCourseManagementPermissionChanged()
    {
        OnPropertyChanged(nameof(CanManageCourses));
        AddCourseCommand.NotifyCanExecuteChanged();
        DeleteCurrentCourseCommand.NotifyCanExecuteChanged();

        if (!HasAnyCourse)
        {
            StatusMessage = EmptyCoursesMessage();
        }
    }

    /// <summary>Whether the active work allows the session to end safely.</summary>
    public bool CanEndSession => CanLogout();

    [RelayCommand(CanExecute = nameof(CanLogout))]
    private void Logout() => LogoutRequested?.Invoke(this, EventArgs.Empty);

    private bool CanLogout() =>
        !IsRecording &&
        !IsBusy &&
        !IsLoading &&
        !IsCountingDown &&
        !IsTestingAudio &&
        !IsPlayingAudioTest;

    /// <summary>Loads the persisted course list when the window first appears.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            LoadAudioInputDevices();
            var existing = await _courseRepository.LoadAsync();
            ReplaceCourses(existing);
            if (existing.Count == 0)
            {
                StatusMessage = EmptyCoursesMessage();
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

    partial void OnIsAdminModeChanged(bool value)
    {
        if (!HasAnyCourse)
        {
            StatusMessage = EmptyCoursesMessage();
        }
    }

    partial void OnSearchTextChanged(string value) => StudentsView.Refresh();

    partial void OnSelectedAudioInputChanged(AudioInputDevice? value)
    {
        _audioRecorder.ClearInputTestRecording();
        HasAudioTestRecording = false;

        if (value is null)
        {
            return;
        }

        _settings.AudioInputDeviceName = value.Name;
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo guardar el micrófono seleccionado", ex.Message);
        }
    }

    partial void OnSelectedStudentChanged(Student? value)
    {
        // Switching students while idle resets the timer and any "time is
        // up" alarm left over from the previous recording, so the teacher
        // starts with a clean clock.
        if (!IsRecording)
        {
            ElapsedTime = TimeSpan.Zero;
            IsTimeUp = false;
        }
    }

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
        {
            ElapsedTime = TimeSpan.Zero;
            IsTimeUp = false;
            _readingTimer.Start();
        }
        else
        {
            _readingTimer.Stop();
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
            StatusMessage = EmptyCoursesMessage();
        }

        // Reading passages are filtered to the course level. Recompute the
        // level, refresh the filtered view, and drop the current text if it
        // no longer belongs to the new level so a mismatched passage can
        // never stay selected.
        _activeReadingLevel = LevelFromCourse(value);
        ReadingTextsView.Refresh();
        if (SelectedReadingText is not null &&
            _activeReadingLevel is not null &&
            SelectedReadingText.Level != _activeReadingLevel.Value)
        {
            SelectedReadingText = null;
        }

        SelectedStudent = null;
        StudentsView.Refresh();
        OnPropertyChanged(nameof(HasNoStudents));
    }

    private bool MatchesReadingLevel(object item)
    {
        if (item is not ReadingText text)
        {
            return false;
        }

        // When the course level cannot be inferred, show every text rather
        // than hiding all of them (fail open for an unrecognized level).
        return _activeReadingLevel is null || text.Level == _activeReadingLevel.Value;
    }

    /// <summary>
    /// Infers the numeric school level (1 or 2) from a course's free-text
    /// level field (for example "Primero Básico", "2do", "1ero A").
    /// Returns <see langword="null"/> when neither level can be recognized.
    /// </summary>
    private static int? LevelFromCourse(Course? course)
    {
        if (course is null)
        {
            return null;
        }

        var normalized = SearchNormalizer.Normalize(course.Level);
        if (normalized.Length == 0)
        {
            return null;
        }

        var isFirst = normalized.Contains("primero") || normalized.Contains('1');
        var isSecond = normalized.Contains("segundo") || normalized.Contains('2');

        // If both markers appear (unexpected), prefer none over a wrong guess.
        if (isFirst && !isSecond)
        {
            return 1;
        }

        if (isSecond && !isFirst)
        {
            return 2;
        }

        return null;
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

        // Normalize both sides so that accents, diacritics, cedillas and
        // case differences are ignored. "max" matches "MÁXIMO"; "ssa"
        // matches "Eça"; "jose" matches "José".
        var needle = SearchNormalizer.Normalize(SearchText.Trim());
        if (needle.Length == 0)
        {
            return true;
        }

        return SearchNormalizer.Normalize(student.Rut).Contains(needle)
            || SearchNormalizer.Normalize(student.FirstName).Contains(needle)
            || SearchNormalizer.Normalize(student.LastName).Contains(needle);
    }

    [RelayCommand(CanExecute = nameof(CanAddCourse))]
    private async Task AddCourseAsync()
    {
        if (!CanManageCourses)
        {
            return;
        }

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

    private bool CanAddCourse() =>
        CanManageCourses && !IsRecording && !IsBusy && Courses.Count < MaxCourses;

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentCourse))]
    private async Task DeleteCurrentCourseAsync()
    {
        if (!CanManageCourses)
        {
            return;
        }

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
        CanManageCourses && SelectedCourse is not null && !IsRecording && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggleAudioTest))]
    private void ToggleAudioTest()
    {
        if (IsTestingAudio)
        {
            StopAudioTest(stoppedAutomatically: false);
            return;
        }

        if (SelectedAudioInput is null)
        {
            return;
        }

        try
        {
            _audioRecorder.StartInputTest(SelectedAudioInput.DeviceNumber);
            HasAudioTestRecording = false;
            IsTestingAudio = true;
            _audioTestTimer.Start();
            StatusMessage = $"Habla para grabar una prueba de hasta {_audioRecorder.InputTestMaximumDuration.TotalSeconds:0} segundos.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo probar el micrófono", ex.Message);
            StatusMessage = "Prueba de micrófono no iniciada.";
        }
    }

    private void StopAudioTest(bool stoppedAutomatically)
    {
        _audioTestTimer.Stop();
        _audioRecorder.StopInputTest();
        IsTestingAudio = false;
        HasAudioTestRecording = _audioRecorder.HasInputTestRecording;
        AudioInputLevel = 0;

        if (!HasAudioTestRecording)
        {
            StatusMessage = "La prueba no capturó audio.";
        }
        else if (stoppedAutomatically)
        {
            StatusMessage = $"Prueba de {_audioRecorder.InputTestMaximumDuration.TotalSeconds:0} segundos finalizada. Puedes escuchar la muestra.";
        }
        else
        {
            StatusMessage = "Prueba finalizada. Puedes escuchar la muestra.";
        }
    }

    private void OnAudioTestTimerTick(object? sender, EventArgs e)
    {
        if (IsTestingAudio)
        {
            StopAudioTest(stoppedAutomatically: true);
        }
    }

    private bool CanToggleAudioTest() =>
        IsTestingAudio ||
        (!IsRecording &&
         !IsPlayingAudioTest &&
         !IsBusy &&
         !IsCountingDown &&
         SelectedAudioInput is not null);

    [RelayCommand(CanExecute = nameof(CanToggleAudioTestPlayback))]
    private void ToggleAudioTestPlayback()
    {
        if (IsPlayingAudioTest)
        {
            _audioRecorder.StopInputTestPlayback();
            return;
        }

        try
        {
            _audioRecorder.PlayInputTestRecording();
            StatusMessage = "Reproduciendo la prueba de micrófono...";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo reproducir la prueba", ex.Message);
            StatusMessage = "La prueba de micrófono no se pudo reproducir.";
        }
    }

    private bool CanToggleAudioTestPlayback() =>
        IsPlayingAudioTest ||
        (HasAudioTestRecording &&
         !IsTestingAudio &&
         !IsRecording &&
         !IsBusy &&
         !IsCountingDown);

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync();
        }
        else
        {
            await RunCountdownAsync();
            StartRecording();
        }
    }

    private bool CanToggleRecording() =>
        !IsBusy && !IsCountingDown && !IsTestingAudio && !IsPlayingAudioTest &&
        (IsRecording ||
            (SelectedStudent is not null &&
             SelectedReadingText is not null &&
             SelectedAudioInput is not null));

    /// <summary>
    /// Shows the on-screen countdown before recording actually starts, so
    /// the student has a moment to get ready. The recorder and its timer
    /// are only started once the countdown reaches the "start now" cue.
    /// </summary>
    private async Task RunCountdownAsync()
    {
        IsCountingDown = true;
        try
        {
            for (var remaining = CountdownSeconds; remaining >= 1; remaining--)
            {
                CountdownText = $"Comenzando a grabar en {remaining}...";
                await Task.Delay(CountdownStepInterval);
            }

            CountdownText = "¡Ahora!";
            await Task.Delay(CountdownCueInterval);
        }
        finally
        {
            IsCountingDown = false;
        }
    }

    private void StartRecording()
    {
        if (SelectedStudent is null || SelectedAudioInput is null)
        {
            return;
        }

        try
        {
            var outputPath = _pathResolver.ResolveFor(SelectedStudent);

            // Recording requires a selected text, so the metadata is
            // always present; it is embedded in the .lra header so the
            // downstream pipeline knows which passage the audio matches.
            var metadata = SelectedReadingText is null
                ? null
                : new RecordingMetadata(SelectedReadingText.Id);

            _audioRecorder.Start(outputPath, SelectedAudioInput.DeviceNumber, metadata);
            StatusMessage = SelectedReadingText is not null
                ? $"Grabando a {SelectedStudent.Name} leyendo \"{SelectedReadingText.Title}\"..."
                : $"Grabando a {SelectedStudent.Name}...";
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
            StatusMessage = "Grabación guardada.";
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

    private void OnInputTestPlaybackStateChanged(object? sender, bool isPlaying)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyInputTestPlaybackState(isPlaying);
        }
        else
        {
            dispatcher.BeginInvoke(() => ApplyInputTestPlaybackState(isPlaying));
        }
    }

    private void ApplyInputTestPlaybackState(bool isPlaying)
    {
        IsPlayingAudioTest = isPlaying;
        if (!isPlaying)
        {
            StatusMessage = "Reproducción de prueba finalizada.";
        }
    }

    private void OnInputLevelChanged(object? sender, float level)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            if (IsTestingAudio)
            {
                AudioInputLevel = Math.Clamp(level * 100, 0, 100);
            }
        }
        else
        {
            dispatcher.BeginInvoke(() =>
            {
                if (IsTestingAudio)
                {
                    AudioInputLevel = Math.Clamp(level * 100, 0, 100);
                }
            });
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

    private async void OnReadingTimerTick(object? sender, EventArgs e)
    {
        if (!IsRecording)
        {
            return;
        }

        var next = ElapsedTime + TickInterval;
        var limit = TimeSpan.FromSeconds(_settings.ReadingTimeLimitSeconds);

        if (next >= limit)
        {
            ElapsedTime = limit;
            IsTimeUp = true;
            _readingTimer.Stop();
            await StopRecordingAsync();
        }
        else
        {
            ElapsedTime = next;
        }
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

    private void LoadAudioInputDevices()
    {
        try
        {
            AudioInputDevices.Clear();
            foreach (var device in _audioRecorder.GetInputDevices())
            {
                AudioInputDevices.Add(device);
            }

            SelectedAudioInput = AudioInputDevices.FirstOrDefault(device =>
                string.Equals(
                    device.Name,
                    _settings.AudioInputDeviceName,
                    StringComparison.OrdinalIgnoreCase)) ?? AudioInputDevices.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AudioInputDevices.Clear();
            SelectedAudioInput = null;
            _dialogService.ShowError("No se pudieron cargar los micrófonos", ex.Message);
        }
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

    private string EmptyCoursesMessage() => CanManageCourses
        ? "Aún no hay cursos. Importa una planilla para comenzar."
        : "No hay cursos cargados. Contacta al distribuidor para recibir tu curso.";
}
