namespace Quotation.Tally.Tests;

public class TallyLoadGuardTests
{
    [Fact]
    public async Task Requests_never_run_at_the_same_time()
    {
        var guard = new TallyLoadGuard { MinimumGap = TimeSpan.Zero };
        var running = 0;
        var maxRunning = 0;
        async Task<int> Request()
        {
            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
            return now;
        }
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => guard.RunAsync(Request, probe: false, CancellationToken.None)));
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Slow_probe_or_timeout_makes_background_work_hold_back_until_tally_is_quick_again()
    {
        var now = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
        var guard = new TallyLoadGuard(() => now) { MinimumGap = TimeSpan.Zero };
        Assert.False(guard.IsBusy);

        await guard.RunAsync(() => { now = now.AddSeconds(5); return Task.FromResult(0); }, probe: true, CancellationToken.None);
        Assert.True(guard.IsBusy);
        Assert.StartsWith("Tally is busy", guard.Describe());

        await guard.RunAsync(() => { now = now.AddMilliseconds(120); return Task.FromResult(0); }, probe: true, CancellationToken.None);
        Assert.False(guard.IsBusy);
        Assert.Equal("Tally answers in 120 ms.", guard.Describe());

        guard.RecordTimeout();
        Assert.True(guard.IsBusy);
        now = now.AddMinutes(1);
        await guard.RunAsync(() => Task.FromResult(0), probe: true, CancellationToken.None); // quick answer after the timeout
        Assert.False(guard.IsBusy);
    }

    [Fact]
    public async Task Waiting_for_an_earlier_request_does_not_count_as_a_timeout()
    {
        var sim = new Sim();
        var guard = new TallyLoadGuard { MinimumGap = TimeSpan.Zero };
        sim.Engine.ResponseDelay = TimeSpan.FromMilliseconds(400);
        TallyXmlClient Client(double seconds) => new(new HttpClient(sim.Handler, disposeHandler: false),
            new TallyConnectionOptions("http://tally-sim:9000", "", TimeSpan.FromSeconds(seconds)), null, guard);
        // Three queued requests of 0.4 s each with a 1 s timeout: each waits for the others but none times out.
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => new TallyCompanyService(Client(1)).GetLoadedCompaniesAsync()));
        Assert.False(guard.IsBusy);
    }
}
