namespace LecturIA.App.Services;

/// <summary>
/// Abstraction over OS dialogs, allowing view models to remain decoupled
/// from WPF specifics and easy to test.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Opens a file picker dialog so the user can select a student list to
    /// import.
    /// </summary>
    /// <returns>
    /// The absolute path of the selected file, or <see langword="null"/>
    /// when the user cancelled the dialog.
    /// </returns>
    string? PickStudentImportFile();

    /// <summary>Displays a modal error dialog.</summary>
    void ShowError(string title, string message);

    /// <summary>Displays a modal informational dialog.</summary>
    void ShowInfo(string title, string message);

    /// <summary>
    /// Displays a modal yes/no confirmation dialog.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the user confirmed, <see langword="false"/>
    /// otherwise.
    /// </returns>
    bool Confirm(string title, string message);
}
