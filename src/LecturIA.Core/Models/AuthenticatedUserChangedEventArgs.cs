namespace LecturIA.Core.Models;

/// <summary>
/// Carries an updated identity obtained while renewing an authenticated session.
/// </summary>
public sealed class AuthenticatedUserChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data for the refreshed identity.</summary>
    public AuthenticatedUserChangedEventArgs(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        User = user;
    }

    /// <summary>Identity contained in the newly validated ID token.</summary>
    public AuthenticatedUser User { get; }
}
