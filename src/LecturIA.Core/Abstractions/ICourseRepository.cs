using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Persistent storage for the courses managed by the application.
/// </summary>
public interface ICourseRepository
{
    /// <summary>
    /// Loads the current list of courses from the underlying storage.
    /// </summary>
    /// <returns>An empty list when no course has been persisted yet.</returns>
    Task<IReadOnlyList<Course>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the persisted list with the supplied collection.
    /// </summary>
    Task SaveAsync(IEnumerable<Course> courses, CancellationToken cancellationToken = default);
}
