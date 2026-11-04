using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Quotation.Contracts;
using Quotation.Server.Infrastructure;

namespace Quotation.Server.Services;

/// <summary>
/// Requests that took longer than <see cref="Threshold"/>: measured by the server (its own time) and reported by the
/// client PCs (the whole time from click to answer, so network delays show too). The last <see cref="Capacity"/> are
/// kept in a small file next to the logs, for Diagnostics → Slow requests.
/// </summary>
public sealed class SlowRequestLog
{
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(1);
    public const int Capacity = 500;
    public const string RequestIdHeader = "X-Request-Id";
    public const string ServerMsHeader = "X-Server-Ms";
    public const string ServerBusyHeader = "X-Server-Busy";

    private readonly string _file;
    private readonly ILogger<SlowRequestLog> _log;
    private readonly Lock _gate = new();
    private readonly List<SlowRequestDto> _entries;
    private int _inFlight;

    public SlowRequestLog(string dataDirectory, ILogger<SlowRequestLog> log)
    {
        _file = Path.Combine(dataDirectory, "logs", "slow-requests.json");
        _log = log;
        _entries = Load();
    }

    /// <summary>Requests being answered right now (shown as "N other requests" when a request was slow).</summary>
    public int InFlight => Volatile.Read(ref _inFlight);
    public void Started() => Interlocked.Increment(ref _inFlight);
    public void Finished() => Interlocked.Decrement(ref _inFlight);

    public IReadOnlyList<SlowRequestDto> Recent()
    {
        lock (_gate) return _entries.OrderByDescending(e => e.WhenUtc).ToList();
    }

    /// <summary>The server's own measurement of a slow request.</summary>
    public void ServerSlow(string id, DateTime whenUtc, string user, string machine, string what, double serverMs, string busy) =>
        Upsert(new SlowRequestDto(id, whenUtc, user, machine, what, null, serverMs, busy));

    /// <summary>A client PC reports a request that took long from click to answer (it may add to the server's entry).</summary>
    public void ClientReport(SlowRequestReport r, string user, string machine, DateTime nowUtc)
    {
        var id = string.IsNullOrWhiteSpace(r.RequestId) ? Guid.NewGuid().ToString("N") : r.RequestId!;
        lock (_gate)
        {
            var existing = _entries.FindIndex(e => e.Id == id);
            if (existing >= 0)
            {
                _entries[existing] = _entries[existing] with { TotalMs = r.TotalMs };
                Save();
                return;
            }
        }
        Upsert(new SlowRequestDto(id, nowUtc.AddMilliseconds(-r.TotalMs), user, machine, Trim(r.What), r.TotalMs, r.ServerMs,
            Trim(r.ServerBusy ?? "")));
    }

    private void Upsert(SlowRequestDto entry)
    {
        lock (_gate)
        {
            var i = _entries.FindIndex(e => e.Id == entry.Id);
            if (i >= 0) _entries[i] = entry with { TotalMs = entry.TotalMs ?? _entries[i].TotalMs };
            else _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
            Save();
        }
    }

    private static string Trim(string s) => s.Length > 120 ? s[..120] : s;

    private List<SlowRequestDto> Load()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize<List<SlowRequestDto>>(File.ReadAllText(_file)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning("Slow request list could not be read: {Message}", ex.Message);
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("Slow request list could not be saved: {Message}", ex.Message);
        }
    }

    /// <summary>"Search products", "Customer details" …: what a request was for, in words.</summary>
    public static string Describe(string method, string path)
    {
        var p = path.ToLowerInvariant();
        string Named() => p switch
        {
            _ when p.StartsWith("/api/catalog/products/search") || p.StartsWith("/api/products/search") => "Search products",
            _ when p.StartsWith("/api/catalog/customers/search") || p.StartsWith("/api/customers/search") => "Search customers",
            _ when p.Contains("/customers/") => "Customer details",
            _ when p.Contains("/products/") => "Product details",
            _ when p.EndsWith("/approve") => "Approve",
            _ when p.Contains("/pdf/sealed") => "Sealed PDF",
            _ when p.EndsWith("/pdf") => "PDF",
            _ when p.EndsWith("/preview") => "Preview",
            _ when p.StartsWith("/api/quotations") && method == "GET" && p.Count(c => c == '/') > 2 => "Open quotation",
            _ when p.StartsWith("/api/quotations") && method == "GET" => "Quotation list / search",
            _ when p.StartsWith("/api/quotations") && method is "POST" or "PUT" => "Save quotation",
            _ when p.StartsWith("/api/status") => "Status check",
            _ when p.StartsWith("/api/dashboard") => "Dashboard",
            _ => "",
        };
        var name = Named();
        return name.Length > 0 ? name : $"{method} {path}";
    }

    /// <summary>What the server was doing besides answering: "Tally sync", "3 other requests".</summary>
    public string Busy(RuntimeStatus runtime)
    {
        var parts = new List<string>();
        if (runtime.SyncRunning) parts.Add("Tally sync");
        var others = InFlight - 1;
        if (others > 0) parts.Add($"{others} other request{(others == 1 ? "" : "s")}");
        return string.Join(", ", parts);
    }
}

/// <summary>Times every API request: adds the server time and a request id to the answer and records slow ones.</summary>
public sealed class SlowRequestMiddleware(RequestDelegate next, SlowRequestLog slow, RuntimeStatus runtime, TimeProvider clock)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api"))
        {
            await next(ctx);
            return;
        }
        var id = Guid.NewGuid().ToString("N")[..12];
        slow.Started();
        var busyAtStart = slow.Busy(runtime);
        var sw = Stopwatch.StartNew();
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[SlowRequestLog.RequestIdHeader] = id;
            ctx.Response.Headers[SlowRequestLog.ServerMsHeader] = ((long)sw.Elapsed.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (busyAtStart.Length > 0) ctx.Response.Headers[SlowRequestLog.ServerBusyHeader] = busyAtStart;
            return Task.CompletedTask;
        });
        try
        {
            await next(ctx);
        }
        finally
        {
            slow.Finished();
            sw.Stop();
            if (sw.Elapsed >= SlowRequestLog.Threshold && !ctx.Request.Path.StartsWithSegments("/api/diagnostics/slow"))
            {
                var busy = string.Join(", ", new[] { busyAtStart, runtime.SyncRunning && !busyAtStart.Contains("Tally sync") ? "Tally sync" : "" }
                    .Where(s => s.Length > 0));
                slow.ServerSlow(id, clock.GetUtcNow().UtcDateTime - sw.Elapsed, ctx.User.UserName(), ctx.Machine(),
                    SlowRequestLog.Describe(ctx.Request.Method, ctx.Request.Path.Value ?? ""), Math.Round(sw.Elapsed.TotalMilliseconds), busy);
            }
        }
    }
}
