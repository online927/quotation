using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Round 41 (DB-1): the numbers of the dashboard for today / this week / this month / this financial year, compared with the
/// period before: value quoted, how many, average, the biggest, top customers (by value and by times quoted), most quoted
/// items, the team, Won / Lost and the activity of the last 17 weeks. Approved quotations only. Read-only.
/// </summary>
public sealed class DashboardStatsService(QuotationDbContext db, FinancialYearService fy, SettingsService settings, TimeProvider clock)
{
    public static readonly string[] Periods = ["today", "week", "month", "fy"];

    private sealed record Row(Guid Id, string? Number, DateOnly Date, DateTime CreatedUtc, decimal Value, string Customer, int? CustomerId,
        string User, QuotationOutcome Outcome, DateOnly? FollowUpDate);

    public async Task<DashboardStatsDto> GetAsync(string? period, string userName, bool isAdmin, CancellationToken ct)
    {
        period = Periods.Contains(period?.ToLowerInvariant()) ? period!.ToLowerInvariant() : "month";
        var today = fy.Today();
        var (from, prevFrom, prevTo) = Range(period, today, fy.GetActive().Year);
        var heatFrom = today.AddDays(-(7 * 16 + (int)today.DayOfWeek));
        var earliest = new[] { prevFrom, heatFrom }.Min();

        var rows = (await db.Quotations.AsNoTracking()
                .Where(x => x.DocType == DocumentType.Quotation
                            && (x.Status == QuotationStatus.Approved || x.Status == QuotationStatus.Generated)
                            && x.Date >= earliest && x.Date <= today)
                .Select(x => new { x.Id, x.Number, x.Date, x.CreatedUtc, x.GrandTotal, x.Currency, x.ExchangeRate, x.Buyer.Name, x.CustomerId,
                    User = x.Owner ?? x.CreatedBy, x.Outcome, x.FollowUpDate })
                .ToListAsync(ct))
            .Select(x => new Row(x.Id, x.Number, x.Date, x.CreatedUtc, InRupees(x.GrandTotal, x.Currency, x.ExchangeRate), x.Name, x.CustomerId,
                x.User, x.Outcome, x.FollowUpDate))
            .ToList();
        var now = rows.Where(r => r.Date >= from && r.Date <= today).ToList();
        var before = rows.Where(r => r.Date >= prevFrom && r.Date <= prevTo).ToList();

        // Trend: 12 equal slices of the period (a day: its hours).
        var (values, counts, averages) = Trend(now, from, today, period);

        var biggest = now.OrderByDescending(r => r.Value).FirstOrDefault();
        var customers = now.GroupBy(r => r.CustomerId?.ToString(CultureInfo.InvariantCulture) ?? r.Customer.Trim().ToUpperInvariant())
            .Select(g => new DashboardCustomerDto(g.OrderByDescending(r => r.CreatedUtc).First().Customer, g.First().CustomerId, g.Sum(r => r.Value), g.Count()))
            .Where(c => c.Name.Length > 0).ToList();

        var ids = now.Select(r => r.Id).ToList();
        var lines = await db.QuotationLines.AsNoTracking().Where(l => ids.Contains(l.QuotationId))
            .Select(l => new { l.QuotationId, l.ItemName, l.ProductId, l.Amount }).ToListAsync(ct);
        var items = lines.GroupBy(l => l.ProductId?.ToString(CultureInfo.InvariantCulture) ?? l.ItemName.Trim().ToUpperInvariant())
            .Select(g => new DashboardItemDto(g.First().ItemName, g.Select(l => l.QuotationId).Distinct().Count(), g.Sum(l => l.Amount)))
            .Where(i => i.Name.Length > 0)
            .OrderByDescending(i => i.Times).ThenByDescending(i => i.Value).Take(5).ToList();

        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Username, u => u.DisplayName, StringComparer.OrdinalIgnoreCase, ct);
        var team = now.GroupBy(r => r.User, StringComparer.OrdinalIgnoreCase)
            .Select(g => (User: g.Key, Count: g.Count(), Value: g.Sum(r => r.Value)))
            .OrderByDescending(t => t.Value).ThenByDescending(t => t.Count)
            .Select((t, i) => new DashboardTeamDto(i + 1, t.User, names.TryGetValue(t.User, out var d) && d.Length > 0 ? d : t.User, t.Count, t.Value,
                string.Equals(t.User, userName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (!isAdmin) team = team.Where(t => t.Me).ToList();

        DashboardOutcomesDto? outcomes = null;
        if (settings.Quotation.TrackOutcomes)
            outcomes = new DashboardOutcomesDto(now.Count(r => r.Outcome == QuotationOutcome.Won), now.Count(r => r.Outcome == QuotationOutcome.Lost),
                now.Count(r => r.Outcome == QuotationOutcome.Open), now.Where(r => r.Outcome == QuotationOutcome.Won).Sum(r => r.Value),
                rows.Count(r => r.Outcome == QuotationOutcome.Open && r.FollowUpDate is { } f && f <= today));

        var perDay = rows.Where(r => r.Date >= heatFrom).GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Count());
        var activity = new List<DashboardDayDto>();
        for (var d = heatFrom; d <= today; d = d.AddDays(1)) activity.Add(new DashboardDayDto(d, perDay.GetValueOrDefault(d)));

        // Streak: working days (Sunday skipped) in a row with a quotation, up to today (or yesterday while today has none yet).
        var streak = 0;
        var day = perDay.ContainsKey(today) ? today : today.AddDays(-1);
        while (true)
        {
            if (day.DayOfWeek == DayOfWeek.Sunday) { day = day.AddDays(-1); continue; }
            if (!perDay.ContainsKey(day)) break;
            streak++;
            day = day.AddDays(-1);
        }

        var tz = TimeZoneInfo.Local;
        var history = rows.Where(r => r.Date >= heatFrom).ToList();
        var busiestDay = history.GroupBy(r => r.Date.DayOfWeek).OrderByDescending(g => g.Count()).Select(g => Plural(g.Key)).FirstOrDefault() ?? "";
        var busiestHours = history.GroupBy(r => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.CreatedUtc, DateTimeKind.Utc), tz).Hour / 2)
            .OrderByDescending(g => g.Count()).Select(g => Hours(g.Key * 2)).FirstOrDefault() ?? "";
        var bestDay = now.GroupBy(r => r.Date.DayOfWeek).OrderByDescending(g => g.Sum(r => r.Value)).Select(g => g.Key.ToString()).FirstOrDefault() ?? "";

        return new DashboardStatsDto(period, from, today,
            now.Sum(r => r.Value), before.Sum(r => r.Value), now.Count, before.Count,
            now.Count == 0 ? 0 : Math.Round(now.Sum(r => r.Value) / now.Count, 0), before.Count == 0 ? 0 : Math.Round(before.Sum(r => r.Value) / before.Count, 0),
            values, counts, averages,
            biggest is null ? null : new DashboardBiggestDto(biggest.Id, biggest.Number ?? "", biggest.Customer, biggest.Value),
            customers.OrderByDescending(c => c.Value).ThenByDescending(c => c.Count).Take(5).ToList(),
            customers.OrderByDescending(c => c.Count).ThenByDescending(c => c.Value).Take(5).ToList(),
            items, team, outcomes, activity, streak, busiestDay, busiestHours, bestDay);
    }

    /// <summary>The period (from … today) and the one before, of the same length.</summary>
    internal static (DateOnly From, DateOnly PrevFrom, DateOnly PrevTo) Range(string period, DateOnly today, FinancialYear year)
    {
        switch (period)
        {
            case "today":
                return (today, today.AddDays(-1), today.AddDays(-1));
            case "week":
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return (monday, monday.AddDays(-7), today.AddDays(-7));
            case "fy":
                var start = year.Contains(today) ? year.StartDate : FinancialYear.For(today, year.StartMonth).StartDate;
                var prevStart = start.AddYears(-1);
                return (start, prevStart, prevStart.AddDays(today.DayNumber - start.DayNumber));
            default:
                var first = new DateOnly(today.Year, today.Month, 1);
                var prevFirst = first.AddMonths(-1);
                var prevTo = prevFirst.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month)) - 1);
                return (first, prevFirst, prevTo);
        }
    }

    private static (List<decimal>, List<int>, List<decimal>) Trend(List<Row> rows, DateOnly from, DateOnly to, string period)
    {
        var values = new decimal[12];
        var counts = new int[12];
        var days = Math.Max(1, to.DayNumber - from.DayNumber + 1);
        foreach (var r in rows)
        {
            var slot = period == "today"
                ? Math.Clamp(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.CreatedUtc, DateTimeKind.Utc), TimeZoneInfo.Local).Hour - 8, 0, 11)
                : Math.Clamp((r.Date.DayNumber - from.DayNumber) * 12 / days, 0, 11);
            values[slot] += r.Value;
            counts[slot]++;
        }
        var averages = values.Select((v, i) => counts[i] == 0 ? 0m : Math.Round(v / counts[i], 0)).ToList();
        return (values.ToList(), counts.ToList(), averages);
    }

    /// <summary>A USD quotation in rupees (its exchange rate); a rupee one as it is.</summary>
    private static decimal InRupees(decimal total, string currency, decimal? exchangeRate) =>
        string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) && exchangeRate is > 0 ? Math.Round(total * exchangeRate.Value, 2) : total;

    private static string Plural(DayOfWeek d) => d + "s";

    private static string Hours(int h)
    {
        static string H(int x) => x == 0 ? "12 am" : x < 12 ? $"{x} am" : x == 12 ? "12 pm" : $"{x - 12} pm";
        return $"{H(h).Split(' ')[0]}–{H((h + 2) % 24)}";
    }
}
