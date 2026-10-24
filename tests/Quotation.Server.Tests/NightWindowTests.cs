using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>The complete read of last rates loads Tally, so it only runs at night.</summary>
public class NightWindowTests : IDisposable
{
    private readonly TestServer _server = new(products: 50, customers: 20);

    public void Dispose() => _server.Dispose();

    private sealed class FixedClock(DateTime localTime) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(localTime, TimeSpan.Zero);
    }

    private SyncWorker WorkerAt(string time)
    {
        _ = _server.CreateClient(); // starts the host
        var sp = _server.Services;
        var clock = new FixedClock(DateTime.Parse("2026-10-24 " + time));
        return new SyncWorker(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<TallySyncService>(),
            sp.GetRequiredService<SettingsService>(), sp.GetRequiredService<RuntimeStatus>(), sp.GetRequiredService<IOptions<ServerOptions>>(),
            sp.GetRequiredService<Quotation.Tally.TallyLoadGuard>(), clock, NullLogger<SyncWorker>.Instance);
    }

    [Fact]
    public async Task Scheduled_syncs_hold_back_while_tally_is_busy()
    {
        var worker = WorkerAt("11:00");
        var sp = _server.Services;
        sp.GetRequiredService<RuntimeStatus>().SetTally(true, "T.SAIFUDDIN & CO.", null, DateTime.UtcNow, 2026);
        Assert.NotNull(await worker.DecideAsync(CancellationToken.None)); // empty database: a full sync is due

        sp.GetRequiredService<Quotation.Tally.TallyLoadGuard>().RecordTimeout(); // Tally did not answer in time
        Assert.Null(await worker.DecideAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("02:30", true)]
    [InlineData("05:59", true)]
    [InlineData("01:59", false)]
    [InlineData("09:30", false)] // Tally opened in the morning: the missed nightly sync runs, but not the voucher read
    [InlineData("16:49", false)]
    public void Last_rates_are_read_automatically_only_after_the_nightly_sync_time(string time, bool expected) =>
        Assert.Equal(expected, WorkerAt(time).IsNightWindow());
}
