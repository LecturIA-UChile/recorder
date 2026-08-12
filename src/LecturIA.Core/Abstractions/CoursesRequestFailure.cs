namespace LecturIA.Core.Abstractions;

/// <summary>
/// Classifies why a request to the courses endpoint could not be completed.
/// </summary>
public enum CoursesRequestFailure
{
    /// <summary>The token was missing, expired or rejected (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The authenticated account is not registered as a professor (HTTP 403).</summary>
    NotAProfessor,

    /// <summary>A transient error prevented the request (timeout, network or HTTP 5xx).</summary>
    Unavailable,
}
