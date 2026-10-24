using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Tally;

namespace Quotation.Server.Services;

public sealed record LastRateResult(int Changed, string Message, bool Failed = false);

/// <summary>
/// "Last rate" for items whose Tally master has no selling price: the rate the item was last quoted or sold at,
/// as TallyPrime itself fills it in. Read (export only) from Tally's Sales and Quotation vouchers, and updated
/// when a quotation is approved in this app. Tally is never changed.
/// <para>
/// TallyPrime answers one request at a time and does not respond to its users meanwhile, so reading vouchers is
/// kept light: vouchers are requested per voucher type through Tally's index, one week at a time with a pause
/// between requests, and reading stops at the first slow answer. The complete read (<see cref="RebuildAsync"/>)
/// only runs when started from the Tally page or by the nightly sync; daytime syncs (<see cref="UpdateAsync"/>)
/// only ask for vouchers created or altered since the last read, from the last two months.
/// </para>
/// </summary>
public sealed class LastRateService(TallyGateway gateway, SettingsService settings, TimeProvider clock, ILogger<LastRateService> log)
{
    public const string StateKey = "state:lastrate";
    public const int ChunkDays = 7;
    public const int RecentMonths = 2;

    /// <summary>Pause between voucher requests, so TallyPrime keeps responding to its users.</summary>
    public TimeSpan PauseBetweenRequests { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Longest a single voucher request may take; slower means Tally is struggling, so reading stops.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>After a failed daytime update, automatic reading pauses for this long.</summary>
    public TimeSpan Backoff { get; set; } = TimeSpan.FromHours(6);

    public sealed class LastRateState
    {
        /// <summary>Settings the stored rates were built with; empty or different = not built yet.</summary>
        public string Hash { get; set; } = "";
        /// <summary>Tally's voucher alteration counter at the last read (unchanged = no new or edited vouchers).</summary>
        public long CompanyVoucherAlterId { get; set; }
        /// <summary>Highest voucher AlterID read so far (daytime updates ask only for newer ones).</summary>
        public long MaxVoucherAlterId { get; set; }
        public DateTime? BuiltUtc { get; set; }
        /// <summary>No automatic reading before this time (set after Tally was slow or refused).</summary>
        public DateTime? PausedUntilUtc { get; set; }
        public string LastError { get; set; } = "";
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

    private string CurrentHash()
    {
        var t = settings.Tally;
        return $"{Math.Clamp(t.LastRateMonths, 1, 120)};{string.Join("|", t.LastRateVoucherTypes.Select(x => x.Trim()))}";
    }

    /// <summary>True when last rates have been read completely with the current settings.</summary>
    public bool IsBuilt => settings.Get<LastRateState>(StateKey).Hash == CurrentHash();

    public const string NotReadYetMessage =
        "Last rates not read yet: use 'Read last rates' on the Tally Connection page (or wait for the nightly sync).";

    /// <summary>
    /// Reads last rates completely (look-back window, week by week). Started from the Tally page or by the nightly sync.
    /// On a slow or failed answer it stops and keeps the previous rates.
    /// </summary>
    public async Task<LastRateResult> RebuildAsync(QuotationDbContext db, long companyVoucherAlterId, DateOnly today,
        Action<string> progress, CancellationToken ct)
    {
        var tally = settings.Tally;
        if (!tally.UseLastRate) return await ClearAllAsync(db, ct);

        var state = settings.Get<LastRateState>(StateKey);
        var months = Math.Clamp(tally.LastRateMonths, 1, 120);
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-months);
        var to = today.AddDays(31);
        var lines = new List<TallyRateLine>();
        IReadOnlyList<string> roots;
        try
        {
            var vouchers = new TallyVoucherService(gateway.CreateClient(RequestTimeout));
            var all = await vouchers.GetVoucherTypesAsync(ct);
            roots = TallyRateVoucherTypes.RequestRoots(all, TallyRateVoucherTypes.Select(all, tally.LastRateVoucherTypes));
            if (roots.Count == 0) return new LastRateResult(0, "Last rates: no Sales or Quotation voucher types found in Tally.", Failed: true);

            var chunks = new List<(DateOnly Start, DateOnly End)>();
            for (var start = from; start <= to; start = start.AddDays(ChunkDays))
            {
                var end = start.AddDays(ChunkDays - 1);
                chunks.Add((start, end > to ? to : end));
            }
            var total = roots.Count * chunks.Count;
            var done = 0;
            foreach (var root in roots)
            {
                for (var i = 0; i < chunks.Count; i++)
                {
                    var (start, end) = chunks[i];
                    if (done > 0 && PauseBetweenRequests > TimeSpan.Zero) await Task.Delay(PauseBetweenRequests, ct);
                    done++;
                    progress($"Reading last rates from Tally ({done} of {total}): {root}, " +
                             $"{start.ToString("dd-MMM-yy", CultureInfo.InvariantCulture)} to {end.ToString("dd-MMM-yy", CultureInfo.InvariantCulture)}…");
                    var got = await vouchers.GetRateLinesAsync(root, start, end, null, ct);
                    if (got.Any(l => l.Date < start || l.Date > end))
                    {
                        // This Tally does not apply the requested period: the answer already holds every voucher of the type.
                        lines.AddRange(got.Where(l => l.Date >= from && l.Date <= to));
                        done += chunks.Count - i - 1;
                        log.LogWarning("Tally ignored the voucher period for {Type}; used one complete answer", root);
                        break;
                    }
                    lines.AddRange(got);
                }
            }
        }
        catch (TallyException ex)
        {
            state.LastError = ex.Message;
            await settings.SaveAsync(StateKey, state, "system", ct);
            log.LogWarning("Reading last rates stopped: {Message}", ex.Message);
            return new LastRateResult(0, $"Reading last rates stopped: {ex.Message} Tally was not changed; the previous last rates are kept. " +
                                         "Try again when Tally is less busy, or reduce the look-back months in Settings → Tally.", Failed: true);
        }

        var changed = await ApplyAsync(db, lines, full: true, ct);
        var maxSeen = lines.Count == 0 ? 0 : lines.Max(l => l.AlterId);
        await settings.SaveAsync(StateKey, new LastRateState
        {
            Hash = CurrentHash(),
            CompanyVoucherAlterId = companyVoucherAlterId,
            MaxVoucherAlterId = Math.Max(maxSeen, state.MaxVoucherAlterId),
            BuiltUtc = clock.GetUtcNow().UtcDateTime,
        }, "system", ct);

        var withRate = await db.Products.CountAsync(p => !p.IsDeleted && p.LastRate != null, ct);
        log.LogInformation("Last rates read: {Lines} voucher lines ({Types}), {Changed} items changed, {WithRate} items have a last rate",
            lines.Count, string.Join(", ", roots), changed, withRate);
        return new LastRateResult(changed, $"Last rates: {withRate} item(s) from {lines.Count} voucher line(s) ({string.Join(", ", roots)} and derived types).");
    }

    /// <summary>
    /// Daytime update during a normal sync: only when the complete read was done, Tally reports new or altered
    /// vouchers, and reading is not paused; asks only for vouchers altered since the last read, from the last months.
    /// </summary>
    public async Task<LastRateResult> UpdateAsync(QuotationDbContext db, long companyVoucherAlterId, DateOnly today, CancellationToken ct)
    {
        var tally = settings.Tally;
        if (!tally.UseLastRate) return await ClearAllAsync(db, ct);
        var state = settings.Get<LastRateState>(StateKey);
        if (state.Hash != CurrentHash()) return new LastRateResult(0, NotReadYetMessage);
        var now = clock.GetUtcNow().UtcDateTime;
        if (state.PausedUntilUtc is { } paused && paused > now) return new LastRateResult(0, "");
        if (companyVoucherAlterId > 0 && companyVoucherAlterId == state.CompanyVoucherAlterId) return new LastRateResult(0, "");

        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-RecentMonths);
        var to = today.AddDays(31);
        var lines = new List<TallyRateLine>();
        try
        {
            var vouchers = new TallyVoucherService(gateway.CreateClient(RequestTimeout));
            var all = await vouchers.GetVoucherTypesAsync(ct);
            var roots = TallyRateVoucherTypes.RequestRoots(all, TallyRateVoucherTypes.Select(all, tally.LastRateVoucherTypes));
            for (var i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (i > 0 && PauseBetweenRequests > TimeSpan.Zero) await Task.Delay(PauseBetweenRequests, ct);
                var got = await vouchers.GetRateLinesAsync(root, from, to, state.MaxVoucherAlterId, ct);
                lines.AddRange(got.Where(l => l.Date >= from && l.Date <= to));
            }
        }
        catch (TallyException ex)
        {
            state.PausedUntilUtc = now + Backoff;
            state.LastError = ex.Message;
            await settings.SaveAsync(StateKey, state, "system", ct);
            log.LogWarning("Last rate update paused: {Message}", ex.Message);
            return new LastRateResult(0, $"Last rate updates paused for a few hours: {ex.Message}", Failed: true);
        }

        var changed = await ApplyAsync(db, lines, full: false, ct);
        state.CompanyVoucherAlterId = companyVoucherAlterId;
        if (lines.Count > 0) state.MaxVoucherAlterId = Math.Max(state.MaxVoucherAlterId, lines.Max(l => l.AlterId));
        state.LastError = "";
        await settings.SaveAsync(StateKey, state, "system", ct);
        return new LastRateResult(changed, changed > 0 ? $"Last rates: {changed} item(s) updated." : "");
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

    private async Task<int> ApplyAsync(QuotationDbContext db, List<TallyRateLine> lines, bool full, CancellationToken ct)
    {
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
