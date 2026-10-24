namespace Quotation.Tally;

/// <summary>
/// Keeps TallyPrime responsive for its users. TallyPrime answers one request at a time and does not react to
/// keyboard or mouse while it works, so all requests from this application go through one guard:
/// <list type="bullet">
/// <item>never more than one request at a time, with a short gap between requests;</item>
/// <item>Tally's response time is measured with the small, regular connection check ("probe");</item>
/// <item>when Tally answers slowly or a request timed out, <see cref="IsBusy"/> tells background work
/// (scheduled syncs, reading last rates) to hold back until Tally answers quickly again.</item>
/// </list>
/// </summary>
public sealed class TallyLoadGuard
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly Func<DateTime> _utcNow;
    private DateTime _lastFinishedUtc = DateTime.MinValue;
    private readonly Lock _gate = new();

    public TallyLoadGuard(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Pause between two requests, so Tally can serve its users in between.</summary>
    public TimeSpan MinimumGap { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>A probe slower than this means Tally is busy (someone runs a heavy report, a big import …).</summary>
    public TimeSpan SlowProbe { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>After a timeout, background work holds back at least this long.</summary>
    public TimeSpan HoldAfterTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Response time of the last probe (small connection-check request).</summary>
    public TimeSpan? LastProbeTime { get; private set; }
    public DateTime? LastProbeUtc { get; private set; }
    public DateTime? LastTimeoutUtc { get; private set; }

    /// <summary>True while background work should not send requests to Tally.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                var now = _utcNow();
                if (LastTimeoutUtc is { } t && now - t < HoldAfterTimeout && (LastProbeUtc is null || LastProbeUtc <= t)) return true;
                return LastProbeTime is { } p && p > SlowProbe;
            }
        }
    }

    /// <summary>Human-readable state for the status screens.</summary>
    public string Describe() => IsBusy
        ? "Tally is busy: background reading is paused until it answers quickly again."
        : LastProbeTime is { } p ? $"Tally answers in {p.TotalMilliseconds:0} ms." : "";

    /// <summary>Runs one request exclusively and measures it.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> request, bool probe, CancellationToken ct)
    {
        await _one.WaitAsync(ct);
        try
        {
            var wait = _lastFinishedUtc + MinimumGap - _utcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            var started = _utcNow();
            var result = await request();
            if (probe) RecordProbe(_utcNow() - started);
            return result;
        }
        finally
        {
            _lastFinishedUtc = _utcNow();
            _one.Release();
        }
    }

    /// <summary>A request to Tally timed out: Tally is struggling.</summary>
    public void RecordTimeout()
    {
        lock (_gate) LastTimeoutUtc = _utcNow();
    }

    private void RecordProbe(TimeSpan elapsed)
    {
        lock (_gate)
        {
            LastProbeTime = elapsed;
            LastProbeUtc = _utcNow();
        }
    }
}
