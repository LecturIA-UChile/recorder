using LecturIA.Core.Abstractions;
using LecturIA.Core.Crypto;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// <see cref="IAdminTokenVerifier"/> backed by a set of embedded Ed25519
/// distributor public keys.
/// </summary>
/// <remarks>
/// A valid token is the base64 encoding of a 64-byte Ed25519 signature
/// over <see cref="AdminTokenPayload.Bytes"/>. The verifier accepts the
/// token if it validates against any of the configured public keys, which
/// is what enables multiple independent distributors: each distributor
/// holds their own private key and the binary embeds every public key.
/// Ed25519 is provided by BouncyCastle because .NET 8 has no in-box
/// implementation.
/// </remarks>
public sealed class Ed25519AdminTokenVerifier : IAdminTokenVerifier
{
    /// <summary>Raw byte length of an Ed25519 signature.</summary>
    private const int SignatureLength = 64;

    private readonly IReadOnlyList<Ed25519PublicKeyParameters> _publicKeys;

    /// <summary>
    /// Creates the verifier over the supplied raw Ed25519 public keys.
    /// </summary>
    /// <param name="publicKeys">Raw 32-byte Ed25519 public keys. An empty
    /// list is allowed and makes every token fail (fail closed).</param>
    /// <exception cref="ArgumentException">
    /// A supplied key is not <see cref="AdminPublicKeySet.PublicKeyLength"/>
    /// bytes long.
    /// </exception>
    public Ed25519AdminTokenVerifier(IReadOnlyList<byte[]> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);

        var parsed = new List<Ed25519PublicKeyParameters>(publicKeys.Count);
        foreach (var key in publicKeys)
        {
            if (key is null || key.Length != AdminPublicKeySet.PublicKeyLength)
            {
                throw new ArgumentException(
                    $"Every admin public key must be {AdminPublicKeySet.PublicKeyLength} bytes.",
                    nameof(publicKeys));
            }

            parsed.Add(new Ed25519PublicKeyParameters(key, 0));
        }

        _publicKeys = parsed;
    }

    /// <inheritdoc />
    public bool Verify(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || _publicKeys.Count == 0)
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(token.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (signature.Length != SignatureLength)
        {
            return false;
        }

        var payload = AdminTokenPayload.Bytes;
        foreach (var publicKey in _publicKeys)
        {
            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, publicKey);
            verifier.BlockUpdate(payload, 0, payload.Length);
            if (verifier.VerifySignature(signature))
            {
                return true;
            }
        }

        return false;
    }
}
