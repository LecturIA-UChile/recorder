using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Imports a list of students from an external file.
/// </summary>
public interface IStudentImporter
{
    /// <summary>
    /// Reads the file at <paramref name="filePath"/> and returns the
    /// normalized list of students it contains.
    /// </summary>
    /// <param name="filePath">Absolute path of the file to import.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="NotSupportedException">The file extension is not supported.</exception>
    /// <exception cref="InvalidDataException">The file does not contain any valid student name.</exception>
    Task<IReadOnlyList<Student>> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}
