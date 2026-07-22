using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using LecturIA.App.Infrastructure;
using LecturIA.Core.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LecturIA.App.Services;

/// <summary>
/// Validates Cognito ID tokens and maps their identity claims to the application model.
/// </summary>
internal sealed class CognitoTokenValidator
{
    private static readonly Dictionary<string, (UserRole Role, int Precedence)> RoleMappings =
        new Dictionary<string, (UserRole Role, int Precedence)>(StringComparer.Ordinal)
        {
            ["Administrator"] = (UserRole.Administrator, 0),
            ["Maintainer"] = (UserRole.Maintainer, 1),
            ["Director"] = (UserRole.Director, 2),
            ["Professor"] = (UserRole.Professor, 3),
        };

    private readonly CognitoOptions _options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public CognitoTokenValidator(CognitoOptions options, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        _options = options;
        var metadataAddress = $"{options.Issuer.AbsoluteUri.TrimEnd('/')}/.well-known/openid-configuration";
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpClient)
            {
                RequireHttps = true,
            });
    }

    public async Task<(AuthenticatedUser User, DateTimeOffset ExpiresAt)> ValidateAsync(
        string idToken,
        string? expectedNonce,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        var result = await ValidateWithConfigurationAsync(idToken, configuration);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            _configurationManager.RequestRefresh();
            configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
            result = await ValidateWithConfigurationAsync(idToken, configuration);
        }

        if (!result.IsValid)
        {
            throw result.Exception is null
                ? new AuthenticationFlowException(
                    "Cognito ID token validation failed without a diagnostic exception.",
                    "Cognito devolvió una identidad que no pudo validarse.")
                : new AuthenticationFlowException(
                    "Cognito ID token validation failed.",
                    "Cognito devolvió una identidad que no pudo validarse.",
                    result.Exception);
        }

        using var payload = DecodePayload(idToken);
        var root = payload.RootElement;
        RequireClaimValue(root, "token_use", "id");

        if (expectedNonce is not null)
        {
            var actualNonce = GetRequiredString(root, "nonce");
            if (!FixedTimeEquals(actualNonce, expectedNonce))
            {
                throw new AuthenticationFlowException(
                    "The Cognito ID token nonce does not match the authorization request.",
                    "La respuesta de inicio de sesión no corresponde a esta solicitud.");
            }
        }

        var subject = GetRequiredString(root, "sub");
        var username = GetRequiredString(root, "cognito:username");
        var displayName = GetOptionalString(root, "name");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = username;
        }

        var role = SelectRole(root);
        var expiration = DateTimeOffset.FromUnixTimeSeconds(GetRequiredInt64(root, "exp"));
        return (new AuthenticatedUser(subject, username, displayName, role), expiration);
    }

    private Task<TokenValidationResult> ValidateWithConfigurationAsync(
        string idToken,
        OpenIdConnectConfiguration configuration)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer.AbsoluteUri.TrimEnd('/'),
            ValidAudience = _options.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        return _tokenHandler.ValidateTokenAsync(idToken, validationParameters);
    }

    private static UserRole SelectRole(JsonElement payload)
    {
        if (!payload.TryGetProperty("cognito:groups", out var groupsElement) ||
            groupsElement.ValueKind != JsonValueKind.Array)
        {
            throw new AuthenticationFlowException(
                "The Cognito ID token does not contain a groups claim.",
                "Tu cuenta no tiene un rol de LecturIA asignado.");
        }

        var recognizedRoles = groupsElement
            .EnumerateArray()
            .Where(group => group.ValueKind == JsonValueKind.String)
            .Select(group => group.GetString())
            .Where(group => group is not null && RoleMappings.ContainsKey(group))
            .Select(group => RoleMappings[group!])
            .OrderBy(mapping => mapping.Precedence)
            .ToList();

        if (recognizedRoles.Count == 0)
        {
            throw new AuthenticationFlowException(
                "The Cognito user does not belong to a recognized application role group.",
                "Tu cuenta no tiene un rol de LecturIA asignado.");
        }

        return recognizedRoles[0].Role;
    }

    private static JsonDocument DecodePayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new AuthenticationFlowException(
                "The Cognito ID token does not have JWT compact serialization.",
                "Cognito devolvió una identidad con un formato inválido.");
        }

        try
        {
            return JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new AuthenticationFlowException(
                "The Cognito ID token payload is not valid JSON.",
                "Cognito devolvió una identidad con un formato inválido.",
                ex);
        }
    }

    private static string GetRequiredString(JsonElement payload, string claimName)
    {
        var value = GetOptionalString(payload, claimName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AuthenticationFlowException(
                $"The Cognito ID token is missing the required claim '{claimName}'.",
                "Cognito no entregó todos los datos requeridos para identificar tu cuenta.");
        }

        return value;
    }

    private static string? GetOptionalString(JsonElement payload, string claimName) =>
        payload.TryGetProperty(claimName, out var claim) && claim.ValueKind == JsonValueKind.String
            ? claim.GetString()
            : null;

    private static long GetRequiredInt64(JsonElement payload, string claimName)
    {
        if (!payload.TryGetProperty(claimName, out var claim) || !claim.TryGetInt64(out var value))
        {
            throw new AuthenticationFlowException(
                $"The Cognito ID token is missing the numeric claim '{claimName}'.",
                "Cognito devolvió una identidad con un formato inválido.");
        }

        return value;
    }

    private static void RequireClaimValue(JsonElement payload, string claimName, string expectedValue)
    {
        var actualValue = GetRequiredString(payload, claimName);
        if (!string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
        {
            throw new AuthenticationFlowException(
                $"The Cognito claim '{claimName}' has the unexpected value '{actualValue}'.",
                "Cognito devolvió un tipo de token que LecturIA no puede aceptar.");
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
