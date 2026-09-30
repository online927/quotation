using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Quotation.Gmail;

public sealed record GmailMessageData(
    string Id,
    string ThreadId,
    string FromAddress,
    string FromName,
    string Subject,
    DateTime ReceivedUtc,
    string BodyText);

/// <summary>Read-only access to one Gmail mailbox.</summary>
public interface IGmailClient
{
    Task<string> GetAccountEmailAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> ListMessageIdsAsync(string query, int max, CancellationToken ct);
    Task<GmailMessageData> GetMessageAsync(string id, CancellationToken ct);
}

/// <summary>
/// Google OAuth 2.0 for installed applications. The user signs in on Google's own page; the app
/// receives an authorization code on a loopback address and exchanges it for a refresh token.
/// The Gmail password is never seen. Scope: read-only.
/// </summary>
public static class GmailOAuth
{
    public static readonly string[] Scopes = [GmailService.Scope.GmailReadonly];

    public static GoogleAuthorizationCodeFlow CreateFlow(string clientSecretJson)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(clientSecretJson));
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = new NullDataStore(),
        });
    }

    /// <summary>Validates the pasted client JSON (must be a "Desktop app" OAuth client).</summary>
    public static string? ValidateClientSecret(string json)
    {
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var s = GoogleClientSecrets.FromStream(stream).Secrets;
            return string.IsNullOrWhiteSpace(s.ClientId) || string.IsNullOrWhiteSpace(s.ClientSecret)
                ? "The OAuth client JSON has no client_id/client_secret."
                : null;
        }
        catch (Exception ex)
        {
            return "The OAuth client JSON could not be read: " + ex.Message;
        }
    }

    public static string BuildAuthorizationUrl(string clientSecretJson, string redirectUri, string state)
    {
        var flow = CreateFlow(clientSecretJson);
        var request = flow.CreateAuthorizationCodeRequest(redirectUri);
        request.State = state;
        var url = request.Build().AbsoluteUri;
        // Ask for a refresh token every time so re-connecting always works.
        return url + (url.Contains("prompt=") ? "" : "&prompt=consent") + (url.Contains("access_type=") ? "" : "&access_type=offline");
    }

    /// <summary>Exchanges the authorization code; returns the refresh token.</summary>
    public static async Task<string> ExchangeCodeAsync(string clientSecretJson, string code, string redirectUri, CancellationToken ct)
    {
        var flow = CreateFlow(clientSecretJson);
        var token = await flow.ExchangeCodeForTokenAsync("gmail", code, redirectUri, ct);
        return token.RefreshToken ?? throw new InvalidOperationException(
            "Google did not return a refresh token. Remove the app's access in your Google account settings and connect again.");
    }
}

/// <summary>Gmail API client using a stored refresh token (access tokens are refreshed automatically).</summary>
public sealed class GoogleGmailClient : IGmailClient, IDisposable
{
    private readonly GmailService _service;

    public GoogleGmailClient(string clientSecretJson, string refreshToken)
    {
        var flow = GmailOAuth.CreateFlow(clientSecretJson);
        var credential = new UserCredential(flow, "gmail", new TokenResponse { RefreshToken = refreshToken });
        _service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "TS Quotation System",
        });
    }

    public async Task<string> GetAccountEmailAsync(CancellationToken ct) =>
        (await _service.Users.GetProfile("me").ExecuteAsync(ct)).EmailAddress;

    public async Task<IReadOnlyList<string>> ListMessageIdsAsync(string query, int max, CancellationToken ct)
    {
        var ids = new List<string>();
        string? page = null;
        do
        {
            var req = _service.Users.Messages.List("me");
            req.Q = query;
            req.MaxResults = Math.Min(100, max - ids.Count);
            req.PageToken = page;
            var res = await req.ExecuteAsync(ct);
            ids.AddRange(res.Messages?.Select(m => m.Id) ?? []);
            page = res.NextPageToken;
        } while (page is not null && ids.Count < max);
        return ids;
    }

    public async Task<GmailMessageData> GetMessageAsync(string id, CancellationToken ct)
    {
        var req = _service.Users.Messages.Get("me", id);
        req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        return GmailParsing.ToData(await req.ExecuteAsync(ct));
    }

    public void Dispose() => _service.Dispose();
}

/// <summary>MIME helpers: headers, plain-text body (falls back to HTML converted to text).</summary>
public static partial class GmailParsing
{
    public static GmailMessageData ToData(Message m)
    {
        var headers = m.Payload?.Headers ?? [];
        string H(string name) => headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var (address, display) = ParseFrom(H("From"));
        var received = m.InternalDate is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : DateTime.UtcNow;
        var body = FindPart(m.Payload, "text/plain") is { } plain
            ? Decode(plain)
            : FindPart(m.Payload, "text/html") is { } html ? HtmlToText(Decode(html)) : m.Snippet ?? "";
        return new GmailMessageData(m.Id, m.ThreadId ?? "", address, display, H("Subject"), received, body.Trim());
    }

    public static (string Address, string Name) ParseFrom(string from)
    {
        var match = AngleAddress().Match(from);
        if (match.Success) return (match.Groups[2].Value.Trim(), match.Groups[1].Value.Trim().Trim('"'));
        return (from.Trim(), "");
    }

    private static MessagePart? FindPart(MessagePart? part, string mime)
    {
        if (part is null) return null;
        if (part.MimeType?.Equals(mime, StringComparison.OrdinalIgnoreCase) == true && part.Body?.Data is not null) return part;
        foreach (var p in part.Parts ?? []) if (FindPart(p, mime) is { } found) return found;
        return null;
    }

    private static string Decode(MessagePart part)
    {
        var data = part.Body.Data.Replace('-', '+').Replace('_', '/');
        data = data.PadRight(data.Length + (4 - data.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(data));
    }

    public static string HtmlToText(string html)
    {
        var s = ScriptOrStyle().Replace(html, "");
        s = LineBreaks().Replace(s, "\n");
        s = Tags().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        return MultiBlank().Replace(s, "\n\n").Trim();
    }

    [GeneratedRegex(@"^(.*?)<([^>]+)>")]
    private static partial Regex AngleAddress();

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<(br|/p|/div|/tr|/li)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex MultiBlank();
}
