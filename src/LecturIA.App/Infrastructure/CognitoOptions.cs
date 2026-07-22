namespace LecturIA.App.Infrastructure;

/// <summary>
/// Non-secret OpenID Connect configuration for the LecturIA Cognito app client.
/// </summary>
public sealed class CognitoOptions
{
    /// <summary>AWS region that hosts the user pool.</summary>
    public string Region { get; } = "us-east-1";

    /// <summary>Identifier of the Cognito user pool.</summary>
    public string UserPoolId { get; } = "us-east-1_7Ftgx4GM9";

    /// <summary>Public app client identifier. Desktop clients do not contain a client secret.</summary>
    public string ClientId { get; } = "5kiggvc6irf3q27emhqkms10ek";

    /// <summary>HTTPS page opened in the system browser to begin sign-in.</summary>
    public Uri LoginUri { get; } = new("https://lecturia.maxfloresv.cl/login");

    /// <summary>Base address of Cognito Managed Login and its OAuth endpoints.</summary>
    public Uri ManagedLoginBaseUri { get; } = new(
        "https://us-east-17ftgx4gm9.auth.us-east-1.amazoncognito.com");

    /// <summary>Expected issuer of ID tokens.</summary>
    public Uri Issuer { get; } = new(
        "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_7Ftgx4GM9");

    /// <summary>Loopback address that receives the authorization response.</summary>
    public Uri CallbackUri { get; } = new("http://127.0.0.1:52187/callback");

    /// <summary>Loopback address that receives the browser after Managed Login logout.</summary>
    public Uri LogoutUri { get; } = new("http://127.0.0.1:52187/logout");

    /// <summary>OpenID Connect scopes requested from Cognito.</summary>
    public IReadOnlyList<string> Scopes { get; } = ["openid", "profile", "email"];
}
