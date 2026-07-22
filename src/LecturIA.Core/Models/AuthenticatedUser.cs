namespace LecturIA.Core.Models;

/// <summary>
/// Provider-neutral identity established for the current application session.
/// </summary>
public sealed class AuthenticatedUser
{
    /// <summary>
    /// Creates an authenticated identity from claims supplied by the active provider.
    /// </summary>
    public AuthenticatedUser(
        string subject,
        string username,
        string displayName,
        UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Subject = subject;
        Username = username;
        DisplayName = displayName;
        Role = role;
    }

    /// <summary>Stable provider identifier, equivalent to the Cognito <c>sub</c> claim.</summary>
    public string Subject { get; }

    /// <summary>Account name shown in the application header.</summary>
    public string Username { get; }

    /// <summary>Human-readable name supplied by the identity provider.</summary>
    public string DisplayName { get; }

    /// <summary>Application role derived from the provider claims.</summary>
    public UserRole Role { get; }
}
