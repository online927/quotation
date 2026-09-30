using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;
using Quotation.Tally;

namespace Quotation.Server.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sync").RequireAuthorization();

        g.MapPost("/{kind}", async (string kind, bool? wait, TallySyncService sync, AuditService audit, HttpContext ctx,
            IHostApplicationLifetime lifetime, ILogger<TallySyncService> log, CancellationToken ct) =>
        {
            if (!Enum.TryParse<SyncKind>(kind, ignoreCase: true, out var k)) return Results.BadRequest(new ApiError("Use 'full' or 'incremental'."));
            if (sync.IsRunning) return Results.Conflict(new ApiError("A synchronization is already running."));
            var user = ctx.User.UserName();
            await audit.WriteAsync(user, ctx.Machine(), "SyncRequested", "Sync", k.ToString(), ct: ct);
            if (wait == true)
            {
                try { return Results.Ok(await sync.RunAsync(k, user, ct)); }
                catch (SyncAlreadyRunningException ex) { return Results.Conflict(new ApiError(ex.Message)); }
            }
            _ = Task.Run(async () =>
            {
                try { await sync.RunAsync(k, user, lifetime.ApplicationStopping); }
                catch (SyncAlreadyRunningException) { }
                catch (Exception ex) { log.LogError(ex, "Background sync failed"); }
            });
            return Results.Accepted();
        });

        g.MapGet("/state", async (QuotationDbContext db, RuntimeStatus runtime, TallySyncService sync, CancellationToken ct) =>
        {
            var last = await db.SyncRuns.OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            var lastOk = await db.SyncRuns.Where(r => r.Status == SyncStatus.Succeeded || r.Status == SyncStatus.Skipped)
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            return new SyncStateDto(sync.IsRunning, runtime.SyncProgress,
                last is null ? null : TallySyncService.ToDto(last), lastOk is null ? null : TallySyncService.ToDto(lastOk));
        });

        g.MapGet("/runs", async (QuotationDbContext db, int? take, CancellationToken ct) =>
            (await db.SyncRuns.OrderByDescending(r => r.Id).Take(Math.Clamp(take ?? 50, 1, 500)).ToListAsync(ct))
                .Select(TallySyncService.ToDto));

        g.MapGet("/sample", async (string type, string name, TallyGateway gateway, CancellationToken ct) =>
        {
            if (type is not ("StockItem" or "Ledger" or "StockGroup" or "Unit")) return Results.BadRequest(new ApiError("Unsupported type."));
            try
            {
                var xml = await new TallyMasterService(gateway.CreateClient()).GetRawSampleAsync(type, name, ct);
                return Results.Text(xml, "text/plain");
            }
            catch (TallyException ex)
            {
                return Results.Json(new ApiError(ex.Message), statusCode: 503);
            }
        }).RequireAuthorization(SystemEndpoints.AdminPolicy);
    }
}
