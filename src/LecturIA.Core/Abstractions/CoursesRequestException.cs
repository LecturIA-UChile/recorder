namespace LecturIA.Core.Abstractions;

/// <summary>
/// Represents a failure while fetching the authenticated professor's courses,
/// with a machine-readable reason and a user-facing message in Spanish.
/// </summary>
public sealed class CoursesRequestException : Exception
{
    /// <summary>Creates a failure without an underlying exception.</summary>
    /// <param name="reason">Category of the failure.</param>
    /// <param name="message">Diagnostic message, in English.</param>
    /// <param name="userMessage">Message shown to the teacher, in Spanish.</param>
    public CoursesRequestException(CoursesRequestFailure reason, string message, string userMessage)
        : base(message)
    {
        Reason = reason;
        UserMessage = userMessage;
    }

    /// <summary>Creates a failure caused by another exception.</summary>
    /// <param name="reason">Category of the failure.</param>
    /// <param name="message">Diagnostic message, in English.</param>
    /// <param name="userMessage">Message shown to the teacher, in Spanish.</param>
    /// <param name="innerException">The exception that triggered this failure.</param>
    public CoursesRequestException(
        CoursesRequestFailure reason,
        string message,
        string userMessage,
        Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
        UserMessage = userMessage;
    }

    /// <summary>Category of the failure, used to decide how to recover.</summary>
    public CoursesRequestFailure Reason { get; }

    /// <summary>Spanish message suitable for displaying to the teacher.</summary>
    public string UserMessage { get; }
}
