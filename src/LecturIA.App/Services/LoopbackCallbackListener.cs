using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using LecturIA.Core.Models;

namespace LecturIA.App.Services;

/// <summary>
/// Receives one OAuth redirect through a temporary listener bound only to IPv4 loopback.
/// </summary>
internal sealed class LoopbackCallbackListener : IAsyncDisposable
{
    private const int MaximumRequestLineLength = 8 * 1024;
    private const int MaximumHeaderCount = 100;
    private readonly Uri _expectedUri;
    private readonly TcpListener _listener;
    private bool _disposed;

    public LoopbackCallbackListener(Uri expectedUri)
    {
        ArgumentNullException.ThrowIfNull(expectedUri);
        if (!string.Equals(expectedUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !string.Equals(expectedUri.Host, IPAddress.Loopback.ToString(), StringComparison.Ordinal) ||
            expectedUri.Port <= 0)
        {
            throw new ArgumentException(
                "The OAuth callback must use an explicit IPv4 loopback HTTP address.",
                nameof(expectedUri));
        }

        _expectedUri = expectedUri;
        _listener = new TcpListener(IPAddress.Loopback, expectedUri.Port);

        try
        {
            _listener.Start(1);
        }
        catch (SocketException ex)
        {
            throw new AuthenticationFlowException(
                $"The loopback port {expectedUri.Port} is unavailable.",
                "No se pudo iniciar el acceso porque el puerto local está ocupado. Cierra otras instancias de LecturIA e inténtalo nuevamente.",
                ex);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> WaitForRequestAsync(
        TimeSpan timeout,
        string browserMessage,
        CancellationToken cancellationToken,
        string? expectedState = null)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(linkedCancellation.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint ||
                    !IPAddress.IsLoopback(remoteEndPoint.Address))
                {
                    continue;
                }

                var request = await ReadRequestAsync(client, linkedCancellation.Token);
                if (request is null)
                {
                    await WriteResponseAsync(client, HttpStatusCode.BadRequest, "Solicitud inválida.", linkedCancellation.Token);
                    continue;
                }

                if (!string.Equals(request.AbsolutePath, _expectedUri.AbsolutePath, StringComparison.Ordinal))
                {
                    await WriteResponseAsync(client, HttpStatusCode.NotFound, "Ruta no encontrada.", linkedCancellation.Token);
                    continue;
                }

                Dictionary<string, string> query;
                try
                {
                    query = ParseQuery(request.Query);
                }
                catch (AuthenticationFlowException)
                {
                    await WriteResponseAsync(client, HttpStatusCode.BadRequest, "Solicitud ambigua.", linkedCancellation.Token);
                    continue;
                }

                if (expectedState is not null &&
                    (!query.TryGetValue("state", out var actualState) ||
                     !FixedTimeEquals(actualState, expectedState)))
                {
                    await WriteResponseAsync(client, HttpStatusCode.BadRequest, "Solicitud no reconocida.", linkedCancellation.Token);
                    continue;
                }

                await WriteResponseAsync(client, HttpStatusCode.OK, browserMessage, linkedCancellation.Token);
                return query;
            }
        }
        catch (OperationCanceledException ex) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationFlowException(
                "The loopback OAuth callback timed out.",
                "El inicio de sesión superó el tiempo de espera. Inténtalo nuevamente.",
                ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _listener.Stop();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<Uri?> ReadRequestAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            client.GetStream(),
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > MaximumRequestLineLength)
        {
            return null;
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !string.Equals(parts[0], "GET", StringComparison.Ordinal) ||
            !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            return null;
        }

        for (var headerIndex = 0; headerIndex < MaximumHeaderCount; headerIndex++)
        {
            var header = await reader.ReadLineAsync(cancellationToken);
            if (header is null || header.Length == 0)
            {
                break;
            }
        }

        var requestTarget = parts[1];
        if (Uri.TryCreate(requestTarget, UriKind.Absolute, out var absoluteTarget))
        {
            return absoluteTarget;
        }

        var authority = _expectedUri.GetLeftPart(UriPartial.Authority);
        return Uri.TryCreate(authority + requestTarget, UriKind.Absolute, out var relativeTarget)
            ? relativeTarget
            : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var queryText = query.TrimStart('?');
        if (queryText.Length == 0)
        {
            return values;
        }

        foreach (var pair in queryText.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var encodedKey = separator >= 0 ? pair[..separator] : pair;
            var encodedValue = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            var key = Uri.UnescapeDataString(encodedKey.Replace('+', ' '));
            var value = Uri.UnescapeDataString(encodedValue.Replace('+', ' '));
            if (!values.TryAdd(key, value))
            {
                throw new AuthenticationFlowException(
                    $"The loopback OAuth callback contains the duplicate parameter '{key}'.",
                    "Cognito devolvió una respuesta ambigua que LecturIA no puede aceptar.");
            }
        }

        return values;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static async Task WriteResponseAsync(
        TcpClient client,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        var body = $"<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>LecturIA</title></head><body><h1>LecturIA</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");

        var stream = client.GetStream();
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
