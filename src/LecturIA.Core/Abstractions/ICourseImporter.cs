using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Imports a course (school, level, section and its students) from an
/// external file produced by the school administration.
/// </summary>
public interface ICourseImporter
{
    /// <summary>
    /// Reads the file at <paramref name="filePath"/> and returns the course
    /// it describes.
    /// </summary>
    /// <param name="filePath">Absolute path of the file to import.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="NotSupportedException">The file extension is not supported.</exception>
    /// <exception cref="InvalidDataException">The file does not contain any valid student record.</exception>
    Task<Course> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}
