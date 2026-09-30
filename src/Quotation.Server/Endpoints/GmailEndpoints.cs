using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Data;
using Quotation.Gmail;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

public static class GmailEndpoints
{
    public static void MapGmailEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gmail").RequireAuthorization();

        g.MapGet("/status", async (SettingsService settings, GmailIngestionService ingestion, QuotationDbContext db, CancellationToken ct) =>
        {
            var gm = settings.Gmail;
            var counts = await db.EmailMessages.GroupBy(e => e.Status).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);
            return new GmailStatusDto(gm.Enabled, settings.HasSecret(SettingsService.SecretGmailClient),
                !string.IsNullOrEmpty(gm.ConnectedAccount) && settings.HasSecret(SettingsService.SecretGmailToken),
                gm.ConnectedAccount, gm.Query, ingestion.LastPollUtc, ingestion.LastError,
                counts.ToDictionary(c => c.Key.ToString(), c => c.Count));
        });

        g.MapGet("/messages", async (QuotationDbContext db, int? take, CancellationToken ct) =>
            await db.EmailMessages.AsNoTracking().OrderByDescending(e => e.Id).Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(e => new EmailMessageDto(e.Id, e.GmailMessageId, e.From, e.Subject, e.ReceivedUtc, e.Status, e.ProcessingResult,
                    e.Attempts, e.LastError, e.AiRequestId, e.QuotationId))
                .ToListAsync(ct));

        g.MapPost("/poll", (GmailIngestionService ingestion, CancellationToken ct) => ingestion.PollAsync(ct));

        var admin = g.MapGroup("").RequireAuthorization(SystemEndpoints.AdminPolicy);
        admin.MapPost("/connect/start", (GmailConnectStartRequest r, GmailConnectService connect) =>
            QuotationEndpoints.Handle(() => Task.FromResult(connect.Start(r.RedirectUri))));

        admin.MapPost("/connect/complete", (GmailConnectCompleteRequest r, GmailConnectService connect, AuditService audit, HttpContext ctx,
            CancellationToken ct) => QuotationEndpoints.Handle(async () =>
        {
            var account = await connect.CompleteAsync(r, (client, token) => new GoogleGmailClient(client, token), ctx.User.UserName(), ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "GmailConnected", "Gmail", account, ct: ct);
            return new { account };
        }));

        admin.MapPost("/disconnect", async (GmailConnectService connect, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            await connect.DisconnectAsync(ctx.User.UserName(), ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "GmailDisconnected", "Gmail", "", ct: ct);
            return Results.NoContent();
        });
    }
}
