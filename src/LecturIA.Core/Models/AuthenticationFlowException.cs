namespace LecturIA.Core.Models;

/// <summary>
/// Represents an authentication failure with separate diagnostic and user-facing messages.
/// </summary>
public sealed class AuthenticationFlowException : Exception
{
    /// <summary>Creates an authentication failure without an underlying exception.</summary>
    public AuthenticationFlowException(string message, string userMessage)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        UserMessage = userMessage;
    }

    /// <summary>Creates an authentication failure caused by another exception.</summary>
    public AuthenticationFlowException(string message, string userMessage, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        UserMessage = userMessage;
    }

    /// <summary>Spanish message that can be displayed without exposing protocol details.</summary>
    public string UserMessage { get; }
}
