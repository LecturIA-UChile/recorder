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
    private readonly ICoursesClient _coursesClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAudioRecorder _audioRecorder;
    private readonly IRecordingPathResolver _pathResolver;
    private readonly IRecordingStatusProvider _recordingStatusProvider;
    private readonly IDialogService _dialogService;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _readingTimer;
    private readonly DispatcherTimer _audioTestTimer;
    private readonly DispatcherTimer _latestSessionRecordingPlaybackTimer;
    private int _availableReadingTextCount;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PlaybackProgressInterval = TimeSpan.FromMilliseconds(250);
    private const int CountdownSeconds = 3;
    private static readonly TimeSpan CountdownStepInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CountdownCueInterval = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Creates the view model with all its required collaborators injected.
    /// </summary>
    public MainViewModel(
        ICourseRepository courseRepository,
        ICourseImporter courseImporter,
        ICoursesClient coursesClient,
        IAuthenticationService authenticationService,
        IAudioRecorder audioRecorder,
        IRecordingPathResolver pathResolver,
        IRecordingStatusProvider recordingStatusProvider,
        IDialogService dialogService,
        IReadingTextProvider readingTextProvider,
        AppSettings settings)
    {
        _courseRepository = courseRepository;
        _courseImporter = courseImporter;
        _coursesClient = coursesClient;
        _authenticationService = authenticationService;
        _audioRecorder = audioRecorder;
        _pathResolver = pathResolver;
        _recordingStatusProvider = recordingStatusProvider;
        _dialogService = dialogService;
        _settings = settings;

        Courses = new ObservableCollection<Course>();
        Courses.CollectionChanged += OnCoursesCollectionChanged;

        Students = new ObservableCollection<StudentListItemViewModel>();
        StudentsView = CollectionViewSource.GetDefaultView(Students);
        StudentsView.Filter = MatchesSearch;

        ReadingTexts = new ObservableCollection<ReadingText>(readingTextProvider.GetAll());
        ReadingTextsView = CollectionViewSource.GetDefaultView(ReadingTexts);
        ReadingTextsView.Filter = MatchesReadingLevel;
        _availableReadingTextCount = ReadingTexts.Count;

        AudioInputDevices = new ObservableCollection<AudioInputDevice>();
        _audioRecorder.StateChanged += OnRecorderStateChanged;
        _audioRecorder.InputLevelChanged += OnInputLevelChanged;
        _audioRecorder.InputTestPlaybackStateChanged += OnInputTestPlaybackStateChanged;
        _audioRecorder.SessionRecordingPlaybackStateChanged += OnSessionRecordingPlaybackStateChanged;
        _recordingStatusProvider.StatusChanged += OnRecordingStatusChanged;
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

        _latestSessionRecordingPlaybackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = PlaybackProgressInterval,
        };
        _latestSessionRecordingPlaybackTimer.Tick += OnLatestSessionRecordingPlaybackTimerTick;
    }

    /// <summary>Courses currently loaded in the application.</summary>
    public ObservableCollection<Course> Courses { get; }

    /// <summary>Students of the <see cref="SelectedCourse"/>, with UI recording status.</summary>
    public ObservableCollection<StudentListItemViewModel> Students { get; }

    /// <summary>The fixed set of reading passages available for recording selection.</summary>
    public ObservableCollection<ReadingText> ReadingTexts { get; }

    /// <summary>Audio input devices currently reported by Windows.</summary>
    public ObservableCollection<AudioInputDevice> AudioInputDevices { get; }

    /// <summary><see langword="true"/> when no audio input device is available.</summary>
    public bool HasNoAudioInputs => AudioInputDevices.Count == 0;

    /// <summary><see langword="true"/> when exactly one audio input device is available.</summary>
    public bool IsSingleAudioInput => AudioInputDevices.Count == 1;

    /// <summary><see langword="true"/> when more than one audio input device is available.</summary>
    public bool HasMultipleAudioInputs => AudioInputDevices.Count > 1;

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
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
    private bool _isPlayingAudioTest;

    /// <summary>Caption of the input test button.</summary>
    public string AudioTestButtonText => IsTestingAudio ? "Detener" : "Probar";

    /// <summary>Caption of the test-sample playback button.</summary>
    public string AudioPlaybackButtonText => IsPlayingAudioTest ? "Detener" : "Escuchar";

    /// <summary>Whether the latest completed recording is retained for this application session.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    private bool _hasLatestSessionRecording;

    /// <summary><see langword="true"/> while the latest session recording is playing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestSessionRecordingPlaybackButtonText))]
    [NotifyPropertyChangedFor(nameof(RecordButtonBlockedReason))]
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
    private bool _isPlayingLatestSessionRecording;

    /// <summary>Caption of the latest session recording playback button.</summary>
    public string LatestSessionRecordingPlaybackButtonText =>
        IsPlayingLatestSessionRecording ? "Detener" : "Reproducir";

    /// <summary>Current position within the latest session recording.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestSessionRecordingPlaybackProgress))]
    [NotifyPropertyChangedFor(nameof(LatestSessionRecordingPlaybackTimeText))]
    private TimeSpan _latestSessionRecordingPlaybackPosition;

    /// <summary>Total duration of the latest session recording.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestSessionRecordingPlaybackProgress))]
    [NotifyPropertyChangedFor(nameof(LatestSessionRecordingPlaybackTimeText))]
    private TimeSpan _latestSessionRecordingPlaybackDuration;

    /// <summary>Latest session recording playback progress from 0 to 100.</summary>
    public double LatestSessionRecordingPlaybackProgress =>
        LatestSessionRecordingPlaybackDuration <= TimeSpan.Zero
            ? 0
            : Math.Clamp(
                LatestSessionRecordingPlaybackPosition.TotalMilliseconds /
                LatestSessionRecordingPlaybackDuration.TotalMilliseconds * 100,
                0,
                100);

    /// <summary>Current and total latest-recording playback times.</summary>
    public string LatestSessionRecordingPlaybackTimeText =>
        $"{FormatPlaybackTime(LatestSessionRecordingPlaybackPosition)} / " +
        FormatPlaybackTime(LatestSessionRecordingPlaybackDuration);

    /// <summary>
    /// View over <see cref="ReadingTexts"/> filtered to the level of the
    /// <see cref="SelectedCourse"/>, so the teacher only sees passages that
    /// match the grade they are recording.
    /// </summary>
    public ICollectionView ReadingTextsView { get; }

    /// <summary><see langword="true"/> when the selected level has no reading passage.</summary>
    public bool HasNoReadingTexts => _availableReadingTextCount == 0;

    /// <summary><see langword="true"/> when the selected level has exactly one reading passage.</summary>
    public bool IsSingleReadingText => _availableReadingTextCount == 1;

    /// <summary><see langword="true"/> when the selected level has multiple reading passages.</summary>
    public bool HasMultipleReadingTexts => _availableReadingTextCount > 1;

    /// <summary>Instruction shown beside the reading value.</summary>
    public string ReadingSelectionHint =>
        IsSingleReadingText ? "Lectura asignada al curso" :
        HasMultipleReadingTexts ? "Selecciona el texto" :
        "Sin lecturas para el curso";

    /// <summary>
    /// Level (1 through 4) inferred from the selected course, or
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

    /// <summary>Currently selected roster item, or <see langword="null"/> when none.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyPropertyChangedFor(nameof(RecordButtonBlockedReason))]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    private StudentListItemViewModel? _selectedStudent;

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
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
    private bool _isRecording;

    /// <summary><see langword="true"/> while an asynchronous operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentCourseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioTestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
    private bool _isBusy;

    /// <summary><see langword="true"/> while the initial data load is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
    [NotifyPropertyChangedFor(nameof(ShowNoStudentsMessage))]
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
    [NotifyCanExecuteChangedFor(nameof(ToggleLatestSessionRecordingPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCoursesCommand))]
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
        : SelectedStudent?.IsRecorded == true
            ? "Reemplazar grabación"
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

            if (IsPlayingLatestSessionRecording)
            {
                return "Detén la reproducción antes de comenzar una nueva grabación.";
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

    /// <summary>
    /// <see langword="true"/> when the empty-roster message should be shown.
    /// Suppressed while a load is in progress so it does not overlap the
    /// loading spinner.
    /// </summary>
    public bool ShowNoStudentsMessage => HasNoStudents && !IsLoading;

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
        _latestSessionRecordingPlaybackTimer.Stop();
        _audioRecorder.ClearLatestSessionRecording();
        HasLatestSessionRecording = false;
        IsPlayingLatestSessionRecording = false;
        LatestSessionRecordingPlaybackPosition = TimeSpan.Zero;
        LatestSessionRecordingPlaybackDuration = TimeSpan.Zero;
        AuthenticatedUser = null;
        AuthenticatedDisplayName = string.Empty;
        AuthenticatedRole = string.Empty;

        // Drop the previous teacher's roster so their students' names do not
        // linger in memory or on screen after the session ends.
        _coursesLoadFailed = false;
        ReplaceCourses(Array.Empty<Course>());

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
        !IsPlayingAudioTest &&
        !IsPlayingLatestSessionRecording;

    /// <summary><see langword="true"/> when courses are sourced from the control plane (regular teacher session).</summary>
    public bool IsProfessorCoursesMode => !IsAdminMode;

    private bool _coursesLoadFailed;

    /// <summary>
    /// Loads the roster when the window first appears. Distributor sessions
    /// keep working with the locally imported courses; regular teacher
    /// sessions fetch their courses from the control plane.
    /// </summary>
    public async Task InitializeAsync()
    {
        LoadAudioInputDevices();

        if (IsAdminMode)
        {
            await LoadLocalCoursesAsync();
        }
        else
        {
            await LoadCoursesFromApiAsync();
        }
    }

    /// <summary>
    /// Reloads the roster for the current session. Called after switching
    /// users, since the window (and this view model) are reused across
    /// sessions and courses are scoped to the signed-in teacher.
    /// </summary>
    public async Task ReloadCoursesAsync()
    {
        if (IsAdminMode)
        {
            await LoadLocalCoursesAsync();
        }
        else
        {
            await LoadCoursesFromApiAsync();
        }
    }

    private async Task LoadLocalCoursesAsync()
    {
        try
        {
            IsLoading = true;
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

    /// <summary>
    /// Fetches the authenticated teacher's courses from the control plane and
    /// replaces the in-memory roster. The response is never persisted to disk
    /// because it carries student names (personal data).
    /// </summary>
    private async Task LoadCoursesFromApiAsync()
    {
        try
        {
            IsLoading = true;
            _coursesLoadFailed = false;
            StatusMessage = "Cargando tus cursos...";

            var accessToken = await _authenticationService.GetAccessTokenAsync();
            var courses = await _coursesClient.GetMyCoursesAsync(accessToken);

            ReplaceCourses(courses);
            StatusMessage = courses.Count == 0
                ? EmptyCoursesMessage()
                : $"{courses.Count} curso(s) cargado(s).";
        }
        catch (CoursesRequestException ex)
        {
            HandleCoursesRequestFailure(ex);
        }
        catch (AuthenticationFlowException ex)
        {
            _coursesLoadFailed = true;
            ReplaceCourses(Array.Empty<Course>());
            StatusMessage = ex.UserMessage;
            _dialogService.ShowInfo("Sesión no válida", ex.UserMessage);
        }
        catch (Exception ex)
        {
            _coursesLoadFailed = true;
            ReplaceCourses(Array.Empty<Course>());
            StatusMessage = "No se pudieron cargar tus cursos.";
            _dialogService.ShowError("Error al cargar tus cursos", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void HandleCoursesRequestFailure(CoursesRequestException ex)
    {
        ReplaceCourses(Array.Empty<Course>());
        StatusMessage = ex.UserMessage;

        switch (ex.Reason)
        {
            case CoursesRequestFailure.NotAProfessor:
                // Retrying cannot fix a missing professor profile.
                _coursesLoadFailed = false;
                _dialogService.ShowInfo("Cuenta sin perfil de profesor", ex.UserMessage);
                break;
            case CoursesRequestFailure.Unauthorized:
                _coursesLoadFailed = true;
                _dialogService.ShowInfo("Sesión no válida", ex.UserMessage);
                break;
            default:
                _coursesLoadFailed = true;
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshCourses))]
    private async Task RefreshCoursesAsync() => await LoadCoursesFromApiAsync();

    private bool CanRefreshCourses() =>
        IsProfessorCoursesMode &&
        !IsLoading &&
        !IsBusy &&
        !IsRecording &&
        !IsCountingDown &&
        !IsTestingAudio &&
        !IsPlayingAudioTest &&
        !IsPlayingLatestSessionRecording;

    partial void OnIsAdminModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsProfessorCoursesMode));
        RefreshCoursesCommand.NotifyCanExecuteChanged();
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

    partial void OnSelectedStudentChanged(StudentListItemViewModel? value)
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
            _recordingStatusProvider.Refresh();
            foreach (var student in value.Students)
            {
                Students.Add(new StudentListItemViewModel(
                    student,
                    _recordingStatusProvider.HasCompletedRecording(student)));
            }

            StatusMessage = $"{value.Students.Count} estudiante(s) en {value.DisplayLabel}.";
        }
        else
        {
            StatusMessage = EmptyCoursesMessage();
        }

        _activeReadingLevel = LevelFromCourse(value);
        UpdateReadingTextOptions();

        StudentsView.Refresh();
        SelectedStudent = Students.Count == 1 ? Students[0] : null;
        OnPropertyChanged(nameof(HasNoStudents));
        OnPropertyChanged(nameof(ShowNoStudentsMessage));
    }

    private void UpdateReadingTextOptions()
    {
        ReadingTextsView.Refresh();
        var availableTexts = ReadingTextsView.Cast<ReadingText>().ToList();
        _availableReadingTextCount = availableTexts.Count;

        OnPropertyChanged(nameof(HasNoReadingTexts));
        OnPropertyChanged(nameof(IsSingleReadingText));
        OnPropertyChanged(nameof(HasMultipleReadingTexts));
        OnPropertyChanged(nameof(ReadingSelectionHint));

        if (availableTexts.Count == 1)
        {
            SelectedReadingText = availableTexts[0];
        }
        else if (SelectedReadingText is not null &&
                 !availableTexts.Contains(SelectedReadingText))
        {
            SelectedReadingText = null;
        }
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
    /// Infers a numeric school level from 1 through 4 from a course's
    /// free-text level field. Returns <see langword="null"/> when exactly one
    /// supported level cannot be identified.
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

        var detectedLevels = new List<int>(4);
        if (normalized.Contains("primero") || normalized.Contains("primer") || normalized.Contains('1'))
        {
            detectedLevels.Add(1);
        }

        if (normalized.Contains("segundo") || normalized.Contains('2'))
        {
            detectedLevels.Add(2);
        }

        if (normalized.Contains("tercero") || normalized.Contains("tercer") || normalized.Contains('3'))
        {
            detectedLevels.Add(3);
        }

        if (normalized.Contains("cuarto") || normalized.Contains('4'))
        {
            detectedLevels.Add(4);
        }

        return detectedLevels.Count == 1 ? detectedLevels[0] : null;
    }

    private bool MatchesSearch(object item)
    {
        if (item is not StudentListItemViewModel student)
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
         !IsPlayingLatestSessionRecording &&
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
         !IsPlayingLatestSessionRecording &&
         !IsBusy &&
         !IsCountingDown);

    [RelayCommand(CanExecute = nameof(CanToggleLatestSessionRecordingPlayback))]
    private void ToggleLatestSessionRecordingPlayback()
    {
        if (IsPlayingLatestSessionRecording)
        {
            _audioRecorder.StopLatestSessionRecordingPlayback();
            return;
        }

        try
        {
            _audioRecorder.PlayLatestSessionRecording();
            StatusMessage = "Reproduciendo la última grabación de esta sesión...";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("No se pudo reproducir la grabación", ex.Message);
            StatusMessage = "La última grabación no se pudo reproducir.";
        }
    }

    private bool CanToggleLatestSessionRecordingPlayback() =>
        IsPlayingLatestSessionRecording ||
        (HasLatestSessionRecording &&
         !IsTestingAudio &&
         !IsPlayingAudioTest &&
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
            if (SelectedStudent?.IsRecorded == true &&
                !_dialogService.Confirm(
                    "Reemplazar evaluación",
                    "Este estudiante ya tiene una evaluación registrada. Si continúas, la nueva grabación reemplazará la anterior y sólo se conservará la grabación más reciente."))
            {
                return;
            }

            await RunCountdownAsync();
            StartRecording();
        }
    }

    private bool CanToggleRecording() =>
        !IsBusy && !IsCountingDown && !IsTestingAudio && !IsPlayingAudioTest &&
        !IsPlayingLatestSessionRecording &&
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
            var outputPath = _pathResolver.ResolveFor(SelectedStudent.Student);

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
        IsBusy = true;
        try
        {
            var recordedStudent = SelectedStudent;
            var wasReplacement = recordedStudent?.IsRecorded == true;
            RecordingResult result;

            try
            {
                result = await _audioRecorder.StopAsync();
                HasLatestSessionRecording = _audioRecorder.HasLatestSessionRecording;
                RefreshLatestSessionRecordingPlaybackProgress();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Error al detener la grabación", ex.Message);
                StatusMessage = "La grabación falló al detenerse.";
                return;
            }

            try
            {
                if (recordedStudent is not null)
                {
                    _recordingStatusProvider.RetainOnlyLatestRecording(
                        recordedStudent.Student,
                        result.FilePath);
                }

                _recordingStatusProvider.Refresh();
            }
            catch (Exception ex)
            {
                if (recordedStudent is not null)
                {
                    recordedStudent.IsRecorded = true;
                    OnPropertyChanged(nameof(RecordButtonText));
                }

                _dialogService.ShowError(
                    "Evaluación guardada con advertencias",
                    $"La nueva grabación se guardó correctamente, pero no se pudo completar el reemplazo de la evaluación anterior. Revisa la carpeta de grabaciones. Detalle: {ex.Message}");
                StatusMessage = "La nueva evaluación se guardó, pero una grabación anterior no se pudo eliminar.";
                return;
            }

            if (recordedStudent is not null)
            {
                recordedStudent.IsRecorded = true;
                OnPropertyChanged(nameof(RecordButtonText));
            }

            StatusMessage = wasReplacement
                ? "Evaluación reemplazada. Sólo se conserva la grabación más reciente. Puedes escucharla mientras LecturIA permanezca abierta."
                : "Evaluación guardada. Puedes escucharla mientras LecturIA permanezca abierta.";
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

    private void OnSessionRecordingPlaybackStateChanged(object? sender, bool isPlaying)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplySessionRecordingPlaybackState(isPlaying);
        }
        else
        {
            dispatcher.BeginInvoke(() => ApplySessionRecordingPlaybackState(isPlaying));
        }
    }

    private void ApplySessionRecordingPlaybackState(bool isPlaying)
    {
        IsPlayingLatestSessionRecording = isPlaying;
        RefreshLatestSessionRecordingPlaybackProgress();

        if (isPlaying)
        {
            _latestSessionRecordingPlaybackTimer.Start();
        }
        else
        {
            _latestSessionRecordingPlaybackTimer.Stop();
            StatusMessage = "Reproducción de la última grabación finalizada.";
        }
    }

    private void OnLatestSessionRecordingPlaybackTimerTick(object? sender, EventArgs e) =>
        RefreshLatestSessionRecordingPlaybackProgress();

    private void RefreshLatestSessionRecordingPlaybackProgress()
    {
        LatestSessionRecordingPlaybackPosition = _audioRecorder.LatestSessionRecordingPosition;
        LatestSessionRecordingPlaybackDuration = _audioRecorder.LatestSessionRecordingDuration;
    }

    private static string FormatPlaybackTime(TimeSpan value)
    {
        var totalSeconds = Math.Max(0, (int)Math.Floor(value.TotalSeconds));
        return $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
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

    private void OnRecordingStatusChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateVisibleRecordingStatuses();
        }
        else
        {
            dispatcher.BeginInvoke(UpdateVisibleRecordingStatuses);
        }
    }

    private void UpdateVisibleRecordingStatuses()
    {
        foreach (var student in Students)
        {
            student.IsRecorded = _recordingStatusProvider.HasCompletedRecording(student.Student);
        }

        OnPropertyChanged(nameof(RecordButtonText));
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
            dispatcher.BeginInvoke(() => ApplyRecorderState(state));
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
            NotifyAudioInputOptionsChanged();
        }
        catch (Exception ex)
        {
            AudioInputDevices.Clear();
            SelectedAudioInput = null;
            NotifyAudioInputOptionsChanged();
            _dialogService.ShowError("No se pudieron cargar los micrófonos", ex.Message);
        }
    }

    private void NotifyAudioInputOptionsChanged()
    {
        OnPropertyChanged(nameof(HasNoAudioInputs));
        OnPropertyChanged(nameof(IsSingleAudioInput));
        OnPropertyChanged(nameof(HasMultipleAudioInputs));
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

    private string EmptyCoursesMessage()
    {
        if (CanManageCourses)
        {
            return "Aún no hay cursos. Importa una planilla para comenzar.";
        }

        if (IsProfessorCoursesMode)
        {
            return _coursesLoadFailed
                ? "No se pudieron cargar tus cursos. Usa \"Actualizar\" para reintentar."
                : "No tienes cursos asignados. Si crees que es un error, contacta al administrador.";
        }

        return "No hay cursos cargados. Contacta al distribuidor para recibir tu curso.";
    }
}
