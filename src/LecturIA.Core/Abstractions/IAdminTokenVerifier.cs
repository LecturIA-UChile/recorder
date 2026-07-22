namespace LecturIA.Core.Abstractions;

/// <summary>
/// Verifies an admin token presented in the in-app distributor login.
/// </summary>
/// <remarks>
/// Implementations validate that the token is a base64-encoded Ed25519
/// signature, computed by a distributor over the well-known
/// <see cref="LecturIA.Core.Crypto.AdminTokenPayload"/> constant, using a
/// private key whose public half is one of the distributor public keys
/// embedded in the application binary. The verifier therefore proves
/// possession of an accepted private key without ever seeing it, and
/// supports multiple distributors: the token is accepted when it verifies
/// against any embedded public key.
/// </remarks>
public interface IAdminTokenVerifier
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="token"/> is a
    /// well-formed base64 Ed25519 signature that verifies against any of
    /// the embedded distributor public keys.
    /// </summary>
    /// <param name="token">User-supplied token text. May contain leading
    /// or trailing whitespace.</param>
    /// <remarks>
    /// Malformed input (invalid base64, wrong signature length, etc.) is
    /// rejected before any cryptographic work is done. When no public
    /// keys are embedded the method always returns <see langword="false"/>
    /// (fail closed): admin mode is simply unreachable.
    /// </remarks>
    bool Verify(string? token);
}
