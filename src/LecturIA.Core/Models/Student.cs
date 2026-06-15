namespace LecturIA.Core.Models;

/// <summary>
/// Represents a student whose oral reading is being recorded for assessment.
/// </summary>
/// <param name="Name">Display name of the student.</param>
public sealed record Student(string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
