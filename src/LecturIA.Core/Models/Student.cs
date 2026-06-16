namespace LecturIA.Core.Models;

/// <summary>
/// Represents a student whose oral reading is being recorded for assessment.
/// </summary>
/// <param name="Rut">Chilean national ID in the format <c>12345678-9</c>.</param>
/// <param name="FirstName">Given name(s) of the student.</param>
/// <param name="LastName">Family name(s) of the student.</param>
public sealed record Student(string Rut, string FirstName, string LastName)
{
    /// <summary>Full display name composed of first and last name.</summary>
    public string Name => $"{FirstName} {LastName}".Trim();

    /// <summary>Display label combining full name and RUT for UI selection contexts.</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Rut)
        ? Name
        : $"{Name} ({Rut})";

    /// <inheritdoc />
    public override string ToString() => Name;
}
