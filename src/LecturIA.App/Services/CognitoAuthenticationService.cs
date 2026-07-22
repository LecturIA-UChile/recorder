using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using LecturIA.App.Infrastructure;
using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;
using Microsoft.IdentityModel.Tokens;

namespace LecturIA.App.Services;

/// <summary>
/// Authenticates users with Cognito Managed Login through Authorization Code and PKCE.
/// </summary>
internal sealed class CognitoAuthenticationService : IAuthenticationService, IDisposable
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshAdvance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CognitoOptions _options;
    private readonly HttpClient _httpClient;
    private readonly CognitoTokenValidator _tokenValidator;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private CancellationTokenSource? _refreshLoopCancellation;
    private Task? _refreshLoopTask;
    private string? _accessToken;
    private string? _idToken;
    private string? _refreshToken;
    private string? _subject;
    private AuthenticatedUser? _currentUser;
    private DateTimeOffset _expiresAt;
    private bool _disposed;

    public CognitoAuthenticationService(
        CognitoOptions options,
        HttpClient httpClient,
        CognitoTokenValidator tokenValidator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenValidator);

        _options = options;
        _httpClient = httpClient;
        _tokenValidator = tokenValidator;
    }

    /// <inheritdoc />
    public event EventHandler? SessionExpired;

    /// <inheritdoc />
    public event EventHandler<AuthenticatedUserChangedEventArgs>? UserChanged;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            StopRefreshLoop();
            ClearSession();

            var state = CreateRandomValue(32);
            var nonce = CreateRandomValue(32);
            var codeVerifier = CreateRandomValue(64);
            var codeChallenge = Base64UrlEncoder.Encode(
                SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

            await using var listener = new LoopbackCallbackListener(_options.CallbackUri);
            OpenBrowser(BuildAuthorizationUri(state, nonce, codeChallenge));

            var callback = await listener.WaitForRequestAsync(
                LoginTimeout,
                "Respuesta recibida. Puedes cerrar esta pestaña y volver a LecturIA.",
                cancellationToken,
                state);

            ValidateAuthorizationCallback(callback, state);
            var code = callback["code"];
            var tokens = await ExchangeAuthorizationCodeAsync(code, codeVerifier, cancellationToken);
            var validatedToken = await _tokenValidator.ValidateAsync(
                tokens.IdToken,
                nonce,
                cancellationToken);

            StoreSession(tokens, validatedToken.User, validatedToken.ExpiresAt);
            StartRefreshLoop();
            return validatedToken.User;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            var refreshToken = _refreshToken;
            StopRefreshLoop();
            ClearSession();

            Exception? remoteLogoutFailure = null;
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                try
                {
                    await RevokeRefreshTokenAsync(refreshToken, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    remoteLogoutFailure = ex;
                }
            }

            try
            {
                await CompleteManagedLogoutAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                remoteLogoutFailure ??= ex;
            }

            if (remoteLogoutFailure is not null)
            {
                throw new AuthenticationFlowException(
                    "The local session ended, but Cognito logout did not complete.",
                    "La sesión local se cerró, pero no fue posible confirmar el cierre en Cognito.",
                    remoteLogoutFailure);
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRefreshLoop();
        ClearSession();
    }

    private Uri BuildAuthorizationUri(string state, string nonce, string codeChallenge)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.CallbackUri.AbsoluteUri,
            ["scope"] = string.Join(' ', _options.Scopes),
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        return BuildUri(_options.LoginUri, parameters);
    }

    private Uri BuildLogoutUri()
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["logout_uri"] = _options.LogoutUri.AbsoluteUri,
        };

        return BuildEndpointUri("logout", parameters);
    }

    private Uri BuildEndpointUri(string relativePath, IReadOnlyDictionary<string, string> parameters)
    {
        var endpoint = new Uri(_options.ManagedLoginBaseUri, relativePath);
        return BuildUri(endpoint, parameters);
    }

    private static Uri BuildUri(Uri endpoint, IReadOnlyDictionary<string, string> parameters)
    {
        var query = string.Join(
            "&",
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    private static void OpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            throw new AuthenticationFlowException(
                "The system browser could not be opened for Cognito authentication.",
                "No se pudo abrir el navegador para iniciar sesión.",
                ex);
        }
    }

    private static void ValidateAuthorizationCallback(
        IReadOnlyDictionary<string, string> callback,
        string expectedState)
    {
        if (callback.TryGetValue("error", out var error))
        {
            throw new AuthenticationFlowException(
                $"Cognito authorization failed with error '{error}'.",
                "Cognito rechazó o canceló el inicio de sesión.");
        }

        if (!callback.TryGetValue("state", out var actualState) ||
            !FixedTimeEquals(actualState, expectedState))
        {
            throw new AuthenticationFlowException(
                "The OAuth callback state does not match the authorization request.",
                "La respuesta de inicio de sesión no corresponde a esta solicitud.");
        }

        if (!callback.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new AuthenticationFlowException(
                "The OAuth callback does not contain an authorization code.",
                "Cognito no devolvió el código necesario para completar el acceso.");
        }
    }

    private async Task<TokenEndpointResponse> ExchangeAuthorizationCodeAsync(
        string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = _options.CallbackUri.AbsoluteUri,
            ["code_verifier"] = codeVerifier,
        };

        var tokens = await RequestTokensAsync(parameters, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            throw new AuthenticationFlowException(
                "Cognito did not return a refresh token for the authorization code grant.",
                "Cognito no entregó una sesión renovable. Revisa la configuración del App Client.");
        }

        return tokens;
    }

    private async Task<TokenEndpointResponse> RequestTokensAsync(
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(parameters);
        var endpoint = new Uri(_options.ManagedLoginBaseUri, "oauth2/token");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadOAuthError(responseBody);
            throw new AuthenticationFlowException(
                $"Cognito token endpoint returned {(int)response.StatusCode} ({error ?? "unknown_error"}).",
                "Cognito no pudo completar la sesión. Inténtalo nuevamente.");
        }

        TokenEndpointResponse? tokens;
        try
        {
            tokens = JsonSerializer.Deserialize<TokenEndpointResponse>(responseBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AuthenticationFlowException(
                "Cognito token endpoint returned invalid JSON.",
                "Cognito devolvió una respuesta inválida.",
                ex);
        }

        if (tokens is null ||
            string.IsNullOrWhiteSpace(tokens.AccessToken) ||
            string.IsNullOrWhiteSpace(tokens.IdToken) ||
            tokens.ExpiresIn <= 0)
        {
            throw new AuthenticationFlowException(
                "Cognito token endpoint omitted required token fields.",
                "Cognito devolvió una sesión incompleta.");
        }

        return tokens;
    }

    private async Task RefreshTokensAsync(CancellationToken cancellationToken)
    {
        AuthenticatedUser? changedUser = null;
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            var refreshToken = _refreshToken;
            var expectedSubject = _subject;
            if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(expectedSubject))
            {
                throw new AuthenticationFlowException(
                    "The in-memory Cognito session does not contain a refresh token or subject.",
                    "La sesión actual ya no puede renovarse.");
            }

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["refresh_token"] = refreshToken,
            };

            var tokens = await RequestTokensAsync(parameters, cancellationToken);
            var validatedToken = await _tokenValidator.ValidateAsync(
                tokens.IdToken,
                expectedNonce: null,
                cancellationToken);
            if (!string.Equals(validatedToken.User.Subject, expectedSubject, StringComparison.Ordinal))
            {
                throw new AuthenticationFlowException(
                    "A refreshed Cognito ID token changed the session subject.",
                    "Cognito devolvió una identidad diferente al renovar la sesión.");
            }

            _accessToken = tokens.AccessToken;
            _idToken = tokens.IdToken;
            if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
            {
                _refreshToken = tokens.RefreshToken;
            }

            _expiresAt = EarlierOf(
                DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn),
                validatedToken.ExpiresAt);

            if (!SameIdentity(_currentUser, validatedToken.User))
            {
                _currentUser = validatedToken.User;
                changedUser = validatedToken.User;
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (changedUser is not null)
        {
            UserChanged?.Invoke(this, new AuthenticatedUserChangedEventArgs(changedUser));
        }
    }

    private void StoreSession(
        TokenEndpointResponse tokens,
        AuthenticatedUser user,
        DateTimeOffset idTokenExpiresAt)
    {
        _accessToken = tokens.AccessToken;
        _idToken = tokens.IdToken;
        _refreshToken = tokens.RefreshToken;
        _subject = user.Subject;
        _currentUser = user;
        _expiresAt = EarlierOf(
            DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn),
            idTokenExpiresAt);
    }

    private void StartRefreshLoop()
    {
        _refreshLoopCancellation = new CancellationTokenSource();
        _refreshLoopTask = RefreshLoopAsync(_refreshLoopCancellation.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = _expiresAt - DateTimeOffset.UtcNow - RefreshAdvance;
                if (delay < TimeSpan.FromSeconds(5))
                {
                    delay = TimeSpan.FromSeconds(5);
                }

                await Task.Delay(delay, cancellationToken);

                try
                {
                    await RefreshTokensAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    if (DateTimeOffset.UtcNow < _expiresAt)
                    {
                        await Task.Delay(RefreshRetryDelay, cancellationToken);
                        continue;
                    }

                    await ExpireSessionAsync(cancellationToken);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExpireSessionAsync(CancellationToken cancellationToken)
    {
        var expired = false;
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (_refreshToken is not null && DateTimeOffset.UtcNow >= _expiresAt)
            {
                ClearSession();
                expired = true;
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (expired)
        {
            SessionExpired?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["client_id"] = _options.ClientId,
        });
        var endpoint = new Uri(_options.ManagedLoginBaseUri, "oauth2/revoke");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Cognito token revocation returned HTTP {(int)response.StatusCode}.");
        }
    }

    private async Task CompleteManagedLogoutAsync(CancellationToken cancellationToken)
    {
        await using var listener = new LoopbackCallbackListener(_options.LogoutUri);
        OpenBrowser(BuildLogoutUri());
        await listener.WaitForRequestAsync(
            LogoutTimeout,
            "La sesión se cerró. Puedes cerrar esta pestaña y volver a LecturIA.",
            cancellationToken);
    }

    private void StopRefreshLoop()
    {
        var refreshLoopTask = _refreshLoopTask;
        _refreshLoopCancellation?.Cancel();
        _refreshLoopCancellation?.Dispose();
        _refreshLoopCancellation = null;
        _refreshLoopTask = null;

        if (refreshLoopTask is not null)
        {
            _ = ObserveRefreshLoopAsync(refreshLoopTask);
        }
    }

    private static async Task ObserveRefreshLoopAsync(Task refreshLoopTask)
    {
        try
        {
            await refreshLoopTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void ClearSession()
    {
        _accessToken = null;
        _idToken = null;
        _refreshToken = null;
        _subject = null;
        _currentUser = null;
        _expiresAt = default;
    }

    private static string CreateRandomValue(int byteCount) =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(byteCount));

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool SameIdentity(AuthenticatedUser? left, AuthenticatedUser right) =>
        left is not null &&
        string.Equals(left.Subject, right.Subject, StringComparison.Ordinal) &&
        string.Equals(left.Username, right.Username, StringComparison.Ordinal) &&
        string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        left.Role == right.Role;

    private static DateTimeOffset EarlierOf(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static string? TryReadOAuthError(string responseBody)
    {
        try
        {
            using var errorDocument = JsonDocument.Parse(responseBody);
            return errorDocument.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class TokenEndpointResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("id_token")]
        public string IdToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
