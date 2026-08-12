using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Reads the courses (with their enrolled students) that belong to the
/// professor identified by the supplied access token.
/// </summary>
/// <remarks>
/// The endpoint derives the professor identity from the token, so no
/// additional credentials are sent. The returned data includes student
/// names (personal data) and is meant to be held in memory only, never
/// persisted or logged.
/// </remarks>
public interface ICoursesClient
{
    /// <summary>
    /// Fetches the authenticated professor's courses and enrolled students.
    /// </summary>
    /// <param name="accessToken">
    /// The provider access token (not the ID token) for the current session.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// The professor's courses, possibly empty when none are assigned.
    /// </returns>
    /// <exception cref="CoursesRequestException">
    /// The request failed. Inspect <see cref="CoursesRequestException.Reason"/>
    /// to decide how to recover.
    /// </exception>
    Task<IReadOnlyList<Course>> GetMyCoursesAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
