using CommunityToolkit.Mvvm.ComponentModel;

using LecturIA.Core.Models;

namespace LecturIA.App.ViewModels;

/// <summary>
/// Presents a student in the roster together with transient recording status.
/// </summary>
public sealed partial class StudentListItemViewModel : ObservableObject
{
    /// <summary>Creates a roster item for a student.</summary>
    public StudentListItemViewModel(Student student, bool isRecorded)
    {
        ArgumentNullException.ThrowIfNull(student);
        Student = student;
        _isRecorded = isRecorded;
    }

    /// <summary>Underlying domain student used by recording services.</summary>
    public Student Student { get; }

    /// <summary>Student RUT displayed in the roster.</summary>
    public string Rut => Student.Rut;

    /// <summary>Student first name displayed in the roster.</summary>
    public string FirstName => Student.FirstName;

    /// <summary>Student last name displayed in the roster.</summary>
    public string LastName => Student.LastName;

    /// <summary>Full student name used in operational messages.</summary>
    public string Name => Student.Name;

    /// <summary>Combined student identity displayed below the roster.</summary>
    public string DisplayLabel => Student.DisplayLabel;

    /// <summary>Whether at least one completed recording exists for this student.</summary>
    [ObservableProperty]
    private bool _isRecorded;
}
