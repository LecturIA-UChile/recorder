using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Establishes and ends user sessions through the configured identity provider.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>Raised when the provider can no longer renew the active session.</summary>
    event EventHandler? SessionExpired;

    /// <summary>Raised when token renewal returns updated identity claims.</summary>
    event EventHandler<AuthenticatedUserChangedEventArgs>? UserChanged;

    /// <summary>Whether this provider can currently start a login flow.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Runs the provider login flow and returns the resulting identity.
    /// </summary>
    /// <returns>The identity established by the provider.</returns>
    Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a valid access token for calling protected APIs on behalf of
    /// the current session, renewing it first when it is about to expire.
    /// </summary>
    /// <returns>The current access token issued by the provider.</returns>
    /// <exception cref="AuthenticationFlowException">
    /// There is no active session, or the token could not be renewed.
    /// </exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends the current provider session.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
