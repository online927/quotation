using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;

namespace Quotation.Server.Services;

public sealed class StatusService(
    QuotationDbContext db,
    RuntimeStatus runtime,
    SettingsService settings,
    FinancialYearService fy,
    IAIProviderFactory ai,
    TimeProvider clock)
{
    public async Task<SystemStatusDto> GetAsync(CancellationToken ct)
    {
        var lastSync = await LastSuccessfulSyncUtcAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = lastSync is null || now - lastSync.Value > TimeSpan.FromHours(settings.Quotation.StaleDataHours);
        var active = fy.GetActive();
        var gmail = settings.Gmail;
        return new SystemStatusDto(
            runtime.TallyConnected,
            runtime.TallyCompany,
            runtime.TallyError,
            runtime.TallyLastCheckedUtc,
            lastSync,
            stale,
            active.Year.Label,
            active.Source,
            await db.Products.CountAsync(p => !p.IsDeleted, ct),
            await db.Customers.CountAsync(c => !c.IsDeleted, ct),
            !string.IsNullOrEmpty(gmail.ConnectedAccount) && settings.HasSecret(SettingsService.SecretGmailToken),
            gmail.ConnectedAccount,
            ai.Create() is not null,
            runtime.SyncRunning || runtime.SyncProgress is not null);
    }

    public async Task<DateTime?> LastSuccessfulSyncUtcAsync(CancellationToken ct) =>
        await db.SyncRuns.Where(r => r.Status == SyncStatus.Succeeded || r.Status == SyncStatus.Skipped)
            .OrderByDescending(r => r.StartedUtc)
            .Select(r => r.FinishedUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var today = fy.Today();
        var todays = await db.Quotations.CountAsync(q => q.Date == today && q.Status != QuotationStatus.Cancelled, ct);
        var drafts = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.Draft, ct);
        var pending = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.PendingReview, ct);
        var inbox = await db.AiRequests.CountAsync(r =>
            r.Status == AiRequestStatus.NeedsClarification || r.Status == AiRequestStatus.Failed || r.Status == AiRequestStatus.ReadyForReview, ct);
        return new DashboardDto(todays, drafts, pending, inbox, await GetAsync(ct));
    }
}
