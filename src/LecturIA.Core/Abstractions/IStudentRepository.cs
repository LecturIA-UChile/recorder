using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Persistent storage for the list of students used by the application.
/// </summary>
public interface IStudentRepository
{
    /// <summary>
    /// Loads the current list of students from the underlying storage.
    /// </summary>
    /// <returns>
    /// An empty list when no students have been persisted yet.
    /// </returns>
    Task<IReadOnlyList<Student>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the persisted list with the supplied collection.
    /// </summary>
    Task SaveAsync(IEnumerable<Student> students, CancellationToken cancellationToken = default);
}
