using System.Text;

namespace LecturIA.Core.Crypto;

/// <summary>
/// Fixed payload signed by a distributor's Ed25519 private key to mint an
/// admin authentication token, and verified by the application against the
/// set of embedded distributor public keys.
/// </summary>
/// <remarks>
/// The payload is intentionally constant. Possession of any accepted
/// private key produces exactly one valid token per key, which the
/// distributor stores in a password manager and pastes into the in-app
/// login dialog. Replay across application binaries that embed the same
/// public key is intentional: the token authenticates the distributor
/// role, not a session or a device.
/// <para>
/// This same constant is duplicated in
/// <c>tools/keys/LecturIA.GenerateKeys/Program.cs</c> because that tool is
/// kept project-reference-free on purpose. Keep both sites in sync.
/// </para>
/// </remarks>
public static class AdminTokenPayload
{
    /// <summary>Versioned identifier of the admin role.</summary>
    public const string Value = "LecturIA.Admin.v1";

    /// <summary>UTF-8 encoded bytes of <see cref="Value"/>.</summary>
    public static byte[] Bytes => Encoding.UTF8.GetBytes(Value);
}
