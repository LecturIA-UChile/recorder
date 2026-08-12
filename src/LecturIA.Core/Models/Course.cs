using System.Globalization;

namespace LecturIA.Core.Models;

/// <summary>
/// Represents a class (curso) led by a teacher: a school, the level and
/// section being taught, and the students enrolled in it.
/// </summary>
/// <remarks>
/// A teacher may be in charge of more than one course at the same time, so
/// the application stores a list of courses and lets the teacher pick which
/// one to record at any given moment.
/// </remarks>
/// <param name="Id">Stable identifier used to reference the course across sessions.</param>
/// <param name="School">Name of the educational establishment.</param>
/// <param name="Level">Grade level (e.g. <c>Primero Básico</c>).</param>
/// <param name="Section">Section letter or label (e.g. <c>A</c>).</param>
/// <param name="Students">Students enrolled in the course.</param>
public sealed record Course(
    Guid Id,
    string School,
    string Level,
    string Section,
    IReadOnlyList<Student> Students)
{
    /// <summary>
    /// Academic year the course belongs to, when known. Populated for
    /// courses fetched from the control plane; <see langword="null"/> for
    /// locally imported courses that do not carry a year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Human-readable label of the course used in selectors and headers.
    /// Combines school with the level and section qualifier when available.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            var qualifier = CourseLabel;
            if (string.IsNullOrWhiteSpace(School))
            {
                return qualifier;
            }

            return $"{School} ({qualifier})";
        }
    }

    /// <summary>
    /// Short label composed of level, section and year when available
    /// (e.g. <c>Primero Básico A - 2026</c>). Falls back to
    /// <c>Curso sin información</c> when no descriptor is present.
    /// </summary>
    public string CourseLabel
    {
        get
        {
            var qualifier = $"{Level} {Section}".Trim();
            if (Year is int year)
            {
                var yearText = year.ToString(CultureInfo.InvariantCulture);
                qualifier = string.IsNullOrWhiteSpace(qualifier)
                    ? yearText
                    : $"{qualifier} - {yearText}";
            }

            return string.IsNullOrWhiteSpace(qualifier) ? "Curso sin información" : qualifier;
        }
    }

    /// <summary>
    /// Name of the school, or an empty string when not available.
    /// </summary>
    public string SchoolLabel => School;

    /// <summary>
    /// Creates a new course with a freshly generated identifier.
    /// </summary>
    public static Course CreateNew(
        string school,
        string level,
        string section,
        IReadOnlyList<Student> students) =>
        new(Guid.NewGuid(), school, level, section, students);
}
