using System.Windows;

using Microsoft.Win32;

namespace LecturIA.App.Services;

/// <summary>
/// WPF-backed implementation of <see cref="IDialogService"/>.
/// </summary>
internal sealed class DialogService : IDialogService
{
    /// <inheritdoc />
    public string? PickStudentImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona la lista de estudiantes",
            Filter = "Planillas y CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <inheritdoc />
    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <inheritdoc />
    public bool Confirm(string title, string message)
    {
        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
