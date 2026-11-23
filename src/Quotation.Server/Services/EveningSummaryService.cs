using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Round 36: the day's summary for the administrator (Settings → Features → Evening summary): quotations approved and
/// their value, per user, won / lost, drafts still open, follow-ups tomorrow. E-mailed at the set time through the Gmail
/// connection (when an address is typed), and shown on the administrator's screen with a WhatsApp button. Tally is not asked.
/// </summary>
public sealed class EveningSummaryService(IServiceScopeFactory scopes, SettingsService settings, IGmailClientFactory gmail, TimeProvider clock,
    ILogger<EveningSummaryService> log)
{
    public const string StateKey = "evening-summary";

    public sealed class SummaryState
    {
        public DateOnly? LastSentDay { get; set; }
        public string? LastSentText { get; set; }
        public string? Error { get; set; }
    }

    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<EveningSummaryDto> BuildAsync(DateOnly day, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var zone = clock.LocalTimeZone;
        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);

        var approved = await db.Quotations.AsNoTracking()
            .Where(q => q.DocType == DocumentType.Quotation && q.ApprovedUtc >= start && q.ApprovedUtc < end
                        && (q.Status == QuotationStatus.Approved || q.Status == QuotationStatus.Generated))
            .Select(q => new { User = q.Owner ?? q.CreatedBy, q.GrandTotal, q.Currency }).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Username, u => u.DisplayName.Length > 0 ? u.DisplayName : u.Username, StringComparer.OrdinalIgnoreCase, ct);
        var won = await db.Quotations.CountAsync(q => q.OutcomeUtc >= start && q.OutcomeUtc < end && q.Outcome == QuotationOutcome.Won, ct);
        var lost = await db.Quotations.CountAsync(q => q.OutcomeUtc >= start && q.OutcomeUtc < end && q.Outcome == QuotationOutcome.Lost, ct);
        var drafts = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.Draft && q.CreatedUtc >= start && q.CreatedUtc < end, ct);
        var tomorrow = day.AddDays(1);
        var followUps = await db.Quotations.CountAsync(q => q.FollowUpDate == tomorrow && q.Outcome == QuotationOutcome.Open
                                                            && (q.Status == QuotationStatus.Approved || q.Status == QuotationStatus.Generated), ct);

        string Rupees(decimal v) => "₹ " + IndianFormat.Amount(v, 0);
        var rupees = approved.Where(a => a.Currency != Currencies.Usd).ToList();
        var title = "Quotations · " + day.ToString("ddd dd MMM", CultureInfo.InvariantCulture);
        var lines = new List<string> { title };
        lines.Add($"📄 {approved.Count} quotation{(approved.Count == 1 ? "" : "s")} · {Rupees(rupees.Sum(a => a.GrandTotal))}"
                  + (approved.Count > rupees.Count ? $" (+{approved.Count - rupees.Count} in USD)" : ""));
        if (approved.Count > 0)
            lines.Add("👤 " + string.Join(" · ", approved.GroupBy(a => a.User, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
                .Select(g => $"{(names.TryGetValue(g.Key, out var n) ? n : g.Key)} {g.Count()} ({Rupees(g.Where(a => a.Currency != Currencies.Usd).Sum(a => a.GrandTotal))})")));
        if (settings.Quotation.TrackOutcomes) lines.Add($"✅ {won} won · ❌ {lost} lost");
        lines.Add($"📝 {drafts} draft{(drafts == 1 ? "" : "s")} still open");
        if (settings.Quotation.TrackOutcomes) lines.Add($"⏰ {followUps} to follow up tomorrow");
        var state = settings.Get<SummaryState>(StateKey);
        return new EveningSummaryDto(day, title, string.Join("\n", lines),
            state.LastSentDay?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture), state.Error);
    }

    /// <summary>The time of day it is due ("19:00"; a wrong value means 19:00).</summary>
    public static TimeOnly DueTime(string? text) =>
        TimeOnly.TryParseExact((text ?? "").Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : new TimeOnly(19, 0);

    /// <summary>Sends today's summary when it is due and not sent yet. Returns true when an e-mail went.</summary>
    public async Task<bool> SendIfDueAsync(CancellationToken ct)
    {
        var qs = settings.Quotation;
        if (!qs.EveningSummary || string.IsNullOrWhiteSpace(qs.EveningSummaryEmail)) return false;
        var now = clock.GetLocalNow().DateTime;
        var today = DateOnly.FromDateTime(now);
        if (TimeOnly.FromDateTime(now) < DueTime(qs.EveningSummaryTime)) return false;
        var state = settings.Get<SummaryState>(StateKey);
        if (state.LastSentDay == today) return false;
        var summary = await BuildAsync(today, ct);
        if (gmail.Create() is not { } client)
        {
            state.Error = "Connect Gmail (Settings → Gmail) to e-mail the evening summary.";
            await settings.SaveAsync(StateKey, state, "system", ct);
            return false;
        }
        try
        {
            await client.SendAsync(qs.EveningSummaryEmail.Trim(), summary.Title, summary.Text, ct);
            state.LastSentDay = today;
            state.LastSentText = summary.Text;
            state.Error = null;
            await settings.SaveAsync(StateKey, state, "system", ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Evening summary e-mail failed: {Message}", ex.Message);
            state.Error = ex.Message.Contains("insufficient", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("scope", StringComparison.OrdinalIgnoreCase)
                ? "Gmail did not allow sending: connect Gmail again once (Google then asks to allow sending)."
                : "The evening summary e-mail failed: " + ex.Message;
            state.LastSentDay = today; // tried once today; no retry every minute
            await settings.SaveAsync(StateKey, state, "system", ct);
            return false;
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }
}

/// <summary>Every minute: the evening summary e-mail when it is due.</summary>
public sealed class EveningSummaryWorker(EveningSummaryService summary, IOptions<ServerOptions> options, ILogger<EveningSummaryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await summary.SendIfDueAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Evening summary failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
