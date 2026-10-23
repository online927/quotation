using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Tally;

namespace Quotation.Server.Services;

public sealed record LastRateResult(int Changed, string Message);

/// <summary>
/// "Last rate" for items whose Tally master has no selling price: the rate the item was last quoted or sold at,
/// as TallyPrime itself fills it in. Read (export only) from Tally's Sales and Quotation vouchers, and updated
/// when a quotation is approved in this app. Tally is never changed.
/// </summary>
public sealed class LastRateService(TallyGateway gateway, SettingsService settings, ILogger<LastRateService> log)
{
    public const string StateKey = "state:lastrate";

    public sealed class LastRateState
    {
        /// <summary>Settings the stored rates were built with; a change forces a full rebuild.</summary>
        public string Hash { get; set; } = "";
        /// <summary>Tally's voucher alteration counter at the last read (unchanged = no new or edited vouchers).</summary>
        public long CompanyVoucherAlterId { get; set; }
        /// <summary>Highest voucher AlterID read so far (incremental reads ask only for newer ones).</summary>
        public long MaxVoucherAlterId { get; set; }
    }

    private sealed record Candidate(DateOnly Date, long Order, decimal Rate, string Note);

    /// <summary>The rate offered for an item: its Tally selling price, or else its last quoted/sold rate.</summary>
    public static (decimal? Rate, DateOnly? Date, string Source) Effective(Product p) =>
        p.Rate is not null || p.LastRate is null
            ? (p.Rate, p.RateDate, p.RateSource)
            : (p.LastRate, p.LastRateDate, "Last rate: " + p.LastRateNote);

    /// <summary>Text shown with a rate taken from an earlier voucher.</summary>
    public static string NoteFor(TallyRateLine l) =>
        $"{l.VoucherType} {l.VoucherNumber} · {l.Party} · {l.Date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture)}".Replace("  ", " ");

    public static string NoteFor(QuotationHeader q) =>
        $"Quotation {q.Number} (this app) · {q.Buyer.Name} · {q.Date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Refreshes last rates from Tally. A full refresh rebuilds them from the whole look-back window; otherwise only
    /// vouchers created or altered since the last read are fetched (nothing at all when Tally reports no voucher change).
    /// </summary>
    public async Task<LastRateResult> RefreshAsync(QuotationDbContext db, bool full, long companyVoucherAlterId, DateOnly today,
        CancellationToken ct)
    {
        var tally = settings.Tally;
        if (!tally.UseLastRate) return await ClearAllAsync(db, ct);

        var months = Math.Clamp(tally.LastRateMonths, 1, 120);
        var hash = $"{months};{string.Join("|", tally.LastRateVoucherTypes.Select(t => t.Trim()))}";
        var state = settings.Get<LastRateState>(StateKey);
        if (state.Hash != hash) full = true;
        if (!full && companyVoucherAlterId > 0 && companyVoucherAlterId == state.CompanyVoucherAlterId)
        {
            return new LastRateResult(0, "");
        }

        var vouchers = new TallyVoucherService(gateway.CreateClient());
        var types = TallyRateVoucherTypes.Select(await vouchers.GetVoucherTypesAsync(ct), tally.LastRateVoucherTypes);
        if (types.Count == 0)
        {
            return new LastRateResult(0, "Last rates: no Sales or Quotation voucher types found in Tally.");
        }

        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-months);
        var to = new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1);
        var lines = new List<TallyRateLine>();
        if (full)
        {
            // One month per request keeps each Tally response small.
            for (var start = from; start <= to; start = start.AddMonths(1))
            {
                var end = start.AddMonths(1).AddDays(-1);
                await foreach (var l in vouchers.GetRateLinesAsync(types, start, end > to ? to : end, null, ct)) lines.Add(l);
            }
        }
        else
        {
            await foreach (var l in vouchers.GetRateLinesAsync(types, from, to, state.MaxVoucherAlterId, ct)) lines.Add(l);
        }

        var products = await db.Products.Where(p => !p.IsDeleted).ToListAsync(ct);
        var byName = products.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var candidates = new Dictionary<int, Candidate>();
        foreach (var l in lines)
        {
            if (!byName.TryGetValue(l.ItemName, out var p)) continue;
            // A rate per alternate unit (e.g. per BOX for an item kept in NOS) is not comparable.
            if (l.Unit.Length > 0 && p.Unit.Length > 0 && !l.Unit.Equals(p.Unit, StringComparison.OrdinalIgnoreCase)) continue;
            Offer(candidates, p.Id, new Candidate(l.Date, l.AlterId, l.Rate, NoteFor(l)));
        }
        if (full)
        {
            // Quotations approved in this app count too (they are not in Tally).
            foreach (var (pid, c) in await AppQuotationCandidatesAsync(db, ct)) Offer(candidates, pid, c);
        }

        var changed = 0;
        foreach (var p in products)
        {
            if (candidates.TryGetValue(p.Id, out var c))
            {
                if (!full && p.LastRateDate is { } existing && c.Date < existing) continue;
                if (p.LastRate == c.Rate && p.LastRateDate == c.Date && p.LastRateNote == c.Note) continue;
                p.LastRate = c.Rate;
                p.LastRateDate = c.Date;
                p.LastRateNote = c.Note;
                changed++;
            }
            else if (full && p.LastRate is not null)
            {
                p.LastRate = null;
                p.LastRateDate = null;
                p.LastRateNote = "";
                changed++;
            }
        }
        await db.SaveChangesAsync(ct);

        var maxSeen = lines.Count == 0 ? 0 : lines.Max(l => l.AlterId);
        await settings.SaveAsync(StateKey, new LastRateState
        {
            Hash = hash,
            CompanyVoucherAlterId = companyVoucherAlterId,
            MaxVoucherAlterId = full ? maxSeen : Math.Max(state.MaxVoucherAlterId, maxSeen),
        }, "system", ct);

        var withRate = products.Count(p => p.LastRate is not null);
        log.LogInformation("Last rates {Mode}: {Lines} voucher lines ({Types}), {Changed} items changed, {WithRate} items have a last rate",
            full ? "rebuilt" : "updated", lines.Count, string.Join(", ", types), changed, withRate);
        var message = full
            ? $"Last rates: {withRate} item(s) from {lines.Count} voucher line(s) ({string.Join(", ", types)})."
            : changed > 0 ? $"Last rates: {changed} item(s) updated." : "";
        return new LastRateResult(changed, message);
    }

    /// <summary>After a quotation is approved in this app its rates become the items' last rates.</summary>
    public async Task<bool> ApplyApprovedQuotationAsync(QuotationDbContext db, QuotationHeader q, CancellationToken ct)
    {
        if (!settings.Tally.UseLastRate || q.Number is null) return false;
        var rates = q.Lines.Where(l => l.ProductId is not null && l.Rate > 0)
            .GroupBy(l => l.ProductId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).Last().Rate);
        if (rates.Count == 0) return false;
        var ids = rates.Keys.ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var note = NoteFor(q);
        var changed = false;
        foreach (var p in products)
        {
            if (p.LastRateDate is { } existing && q.Date < existing) continue;
            p.LastRate = rates[p.Id];
            p.LastRateDate = q.Date;
            p.LastRateNote = note;
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
        return changed;
    }

    private static void Offer(Dictionary<int, Candidate> best, int productId, Candidate c)
    {
        if (!best.TryGetValue(productId, out var current) || c.Date > current.Date || (c.Date == current.Date && c.Order >= current.Order))
        {
            best[productId] = c;
        }
    }

    private static async Task<List<(int ProductId, Candidate Candidate)>> AppQuotationCandidatesAsync(QuotationDbContext db, CancellationToken ct)
    {
        var rows = await (from l in db.QuotationLines
                          join q in db.Quotations on l.QuotationId equals q.Id
                          where l.ProductId != null && l.Rate > 0 && q.Number != null &&
                                (q.Status == QuotationStatus.Approved || q.Status == QuotationStatus.Generated)
                          select new { ProductId = l.ProductId!.Value, l.Rate, q.Date, q.Number, Party = q.Buyer.Name, q.ApprovedUtc })
            .ToListAsync(ct);
        // Ordered after Tally vouchers of the same day (ticks are far above Tally AlterIDs), latest approval last.
        return rows.Select(r => (r.ProductId, new Candidate(r.Date, r.ApprovedUtc?.Ticks ?? 0, r.Rate,
                $"Quotation {r.Number} (this app) · {r.Party} · {r.Date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture)}")))
            .ToList();
    }

    private static async Task<LastRateResult> ClearAllAsync(QuotationDbContext db, CancellationToken ct)
    {
        var withRates = await db.Products.Where(p => p.LastRate != null).ToListAsync(ct);
        foreach (var p in withRates)
        {
            p.LastRate = null;
            p.LastRateDate = null;
            p.LastRateNote = "";
        }
        if (withRates.Count > 0) await db.SaveChangesAsync(ct);
        return new LastRateResult(withRates.Count, withRates.Count > 0 ? "Last rates switched off in Settings." : "");
    }
}
