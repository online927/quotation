using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Core.Domain;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Schedules synchronization: a full sync when the database is empty, incremental syncs every
/// few minutes while Tally is connected, and a nightly full sync (catches anything missed).
/// </summary>
public sealed class SyncWorker(
    IServiceScopeFactory scopes,
    TallySyncService sync,
    SettingsService settings,
    RuntimeStatus runtime,
    IOptions<ServerOptions> options,
    TimeProvider clock,
    ILogger<SyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var kind = await DecideAsync(stoppingToken);
                if (kind is not null && !sync.IsRunning) await sync.RunAsync(kind.Value, "scheduler", stoppingToken);
            }
            catch (SyncAlreadyRunningException) { }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Scheduled sync failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    internal async Task<SyncKind?> DecideAsync(CancellationToken ct)
    {
        if (!runtime.TallyConnected) return null;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var tally = settings.Tally;
        var now = clock.GetLocalNow();

        if (!await db.Products.AnyAsync(ct)) return SyncKind.Full;

        var lastFull = await db.SyncRuns.Where(r => r.Kind == SyncKind.Full && r.Status == SyncStatus.Succeeded)
            .OrderByDescending(r => r.Id).Select(r => (DateTime?)r.StartedUtc).FirstOrDefaultAsync(ct);
        if (TimeOnly.TryParseExact(tally.NightlyFullSyncTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var nightly))
        {
            var dueLocal = now.Date + nightly.ToTimeSpan();
            if (now.DateTime >= dueLocal)
            {
                var dueUtc = new DateTimeOffset(dueLocal, now.Offset).UtcDateTime;
                if (lastFull is null || lastFull < dueUtc) return SyncKind.Full;
            }
        }

        var lastAny = await db.SyncRuns.OrderByDescending(r => r.Id).Select(r => (DateTime?)r.StartedUtc).FirstOrDefaultAsync(ct);
        if (lastAny is null || clock.GetUtcNow().UtcDateTime - lastAny >= TimeSpan.FromMinutes(Math.Max(1, tally.IncrementalSyncMinutes)))
        {
            return SyncKind.Incremental;
        }
        return null;
    }
}
