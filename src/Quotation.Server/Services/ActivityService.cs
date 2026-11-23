using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Round 36: the "Who did what" list for administrators, made from the audit log of one day (server's local time):
/// time, user, a chip for what was done ("Approved", "Cancelled", "Sealed PDF" …) and the quotation with its customer.
/// </summary>
public sealed class ActivityService(QuotationDbContext db, TimeProvider clock)
{
    public async Task<List<ActivityDto>> DayAsync(DateOnly day, string? user, CancellationToken ct)
    {
        var zone = clock.LocalTimeZone;
        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var q = db.AuditLog.AsNoTracking().Where(a => a.AtUtc >= start && a.AtUtc < end);
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.User == user);
        var rows = await q.OrderBy(a => a.AtUtc).ThenBy(a => a.Id).Take(2000).ToListAsync(ct);

        var ids = rows.Where(a => a.EntityType == "Quotation").Select(a => Guid.TryParse(a.EntityId, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().ToList();
        var docs = await db.Quotations.AsNoTracking().Where(h => ids.Contains(h.Id))
            .Select(h => new { h.Id, h.Number, Buyer = h.Buyer.Name, h.GrandTotal, h.Status })
            .ToDictionaryAsync(h => h.Id, ct);

        var list = new List<ActivityDto>();
        foreach (var a in rows)
        {
            if (Describe(a.Action) is not { } chip) continue;
            var what = a.Details;
            if (a.EntityType == "Quotation" && Guid.TryParse(a.EntityId, out var id) && docs.TryGetValue(id, out var d))
            {
                var parts = new List<string> { d.Number ?? "(not numbered)" };
                if (d.Buyer.Trim().Length > 0) parts.Add(d.Buyer.Trim());
                if (a.Action.StartsWith("QuotationApproved", StringComparison.Ordinal) && d.GrandTotal > 0)
                    parts.Add("₹ " + d.GrandTotal.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-IN")));
                what = string.Join(" · ", parts);
            }
            else if (a.EntityType != "Quotation" && a.EntityId.Length > 0 && a.Details.Length == 0)
            {
                what = a.EntityId;
            }
            list.Add(new ActivityDto(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(a.AtUtc, DateTimeKind.Utc), zone), a.User, chip.Chip, chip.Kind, what));
        }
        return list;
    }

    /// <summary>The chip for an audit action; null = not shown (sign-ins and the server's own work stay in Diagnostics).</summary>
    public static (string Chip, string Kind)? Describe(string action) => action switch
    {
        "QuotationCreated" => ("Created", "plain"),
        "QuotationUpdated" => ("Saved", "plain"),
        "QuotationApproved" or "QuotationApprovedWithStaleData" => ("Approved", "ok"),
        "QuotationEditedAfterApproval" => ("Edited", "warn"),
        "QuotationCancelled" => ("Cancelled", "bad"),
        "QuotationDeleted" or "QuotationDeletedPermanently" => ("Deleted", "bad"),
        "QuotationRestored" => ("Restored", "ok"),
        "QuotationDuplicated" => ("Duplicated", "plain"),
        "QuotationOutcome" => ("Won / Lost", "warn"),
        "PdfDownloaded" => ("PDF", "plain"),
        "SettingsChanged" or "NumberSeriesChanged" or "SealPositionChanged" => ("Settings", "plain"),
        "UserChanged" or "UserDeleted" => ("Users", "warn"),
        "DatabaseExported" or "DatabaseBackup" or "DriveBackup" or "MonthPdfsDownloaded" => ("Backup", "plain"),
        "AiReadPhoto" => ("Photo read", "plain"),
        "DatasheetAdded" or "DatasheetRemoved" => ("Datasheet", "plain"),
        _ => null,
    };
}
