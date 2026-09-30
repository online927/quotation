using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Quotation.Desktop.Services;

/// <summary>
/// Receives the OAuth redirect from Google on http://127.0.0.1:{free port}/ (installed-app flow).
/// The browser shows Google's own sign-in page; only the one-time authorization code comes back here.
/// </summary>
public sealed class LoopbackOAuthReceiver : IDisposable
{
    private readonly HttpListener _listener = new();

    public string RedirectUri { get; }

    public LoopbackOAuthReceiver()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        RedirectUri = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(RedirectUri);
        _listener.Start();
    }

    public async Task<(string? Code, string? State, string? Error)> WaitAsync(TimeSpan timeout)
    {
        var contextTask = _listener.GetContextAsync();
        if (await Task.WhenAny(contextTask, Task.Delay(timeout)) != contextTask) return (null, null, "Timed out waiting for Google sign-in.");
        var ctx = await contextTask;
        var q = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url?.Query ?? "");
        var error = q["error"];
        var html = error is null
            ? "<html><body style='font-family:sans-serif'><h2>Gmail connected</h2><p>You can close this window and return to the Quotation System.</p></body></html>"
            : $"<html><body style='font-family:sans-serif'><h2>Gmail was not connected</h2><p>{WebUtility.HtmlEncode(error)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
        return (q["code"], q["state"], error);
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        _listener.Close();
    }
}
