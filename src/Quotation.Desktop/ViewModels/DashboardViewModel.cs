using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase, IReturnablePage
{
    /// <summary>"Today at a glance", or "Eid Mubarak!  ·  Today at a glance" / "Happy Diwali!  ·  …" on the festival days.</summary>
    public string GlanceTitle => FestivalLooks.GlanceTitle;
    public bool IsFestival => ThemeService.Festival is not null;

    /// <summary>The festival look came on or went off.</summary>
    public void FestivalChanged()
    {
        OnPropertyChanged(nameof(GlanceTitle));
        OnPropertyChanged(nameof(IsFestival));
        OnPropertyChanged(nameof(DateLine));
    }

    // ================================================================= Round 41 (DB-1): the new dashboard

    /// <summary>"FRIDAY · 27 NOVEMBER 2026" (on a festival day "EID MUBARAK! · …").</summary>
    public string DateLine
    {
        get
        {
            var date = DateTime.Today.ToString("dddd · d MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            var festive = FestivalLooks.GlanceTitle;
            var greet = festive.Contains('·') ? festive.Split('·')[0].Trim().ToUpperInvariant() : null;
            return greet is { Length: > 0 } ? $"{greet} · {date}" : date;
        }
    }

    [ObservableProperty] private string _period = "month";
    [ObservableProperty] private DashboardStatsDto? _stats;
    [ObservableProperty] private bool _customersByCount;

    public bool PeriodToday { get => Period == "today"; set { if (value) Period = "today"; } }
    public bool PeriodWeek { get => Period == "week"; set { if (value) Period = "week"; } }
    public bool PeriodMonth { get => Period == "month"; set { if (value) Period = "month"; } }
    public bool PeriodFy { get => Period == "fy"; set { if (value) Period = "fy"; } }
    public bool CustomersByValue { get => !CustomersByCount; set { if (value) CustomersByCount = false; } }

    private string PeriodWord => Period switch { "today" => "today", "week" => "this week", "fy" => "this year", _ => "this month" };
    private string PreviousWord => Period switch
    {
        "today" => "yesterday",
        "week" => "last week",
        "fy" => "last year",
        _ => DateTime.Today.AddMonths(-1).ToString("MMM", CultureInfo.InvariantCulture),
    };
    public string ValueLabel => "Quoted " + PeriodWord;

    /// <summary>"You've quoted ₹ 28.4 lakh this month — ▲ 18% on October. Best day: Thursday."</summary>
    public string Summary
    {
        get
        {
            if (Stats is not { } s) return "";
            if (s.Count == 0) return $"Nothing approved {PeriodWord} yet.";
            var text = $"Quoted {Short(s.QuotedValue, words: true)} {PeriodWord}";
            if (s.PreviousValue > 0) text += $" — {Change(s.QuotedValue, s.PreviousValue)} on {(Period == "month" ? DateTime.Today.AddMonths(-1).ToString("MMMM", CultureInfo.InvariantCulture) : PreviousWord)}";
            if (Period != "today" && s.BestDay.Length > 0) text += $". Best day: {s.BestDay}";
            return text + ".";
        }
    }

    public string ValueText => Stats is { } s ? Short(s.QuotedValue) : "–";
    public string CountText => Stats?.Count.ToString(CultureInfo.InvariantCulture) ?? "–";
    public string AverageText => Stats is { } s ? Money(s.AverageValue) : "–";
    public string ValueChange => Stats is { PreviousValue: > 0 } s ? $"{Change(s.QuotedValue, s.PreviousValue)} vs {PreviousWord}" : "";
    public string CountChange => Stats is { } s && (s.Count != 0 || s.PreviousCount != 0)
        ? (s.Count >= s.PreviousCount ? "▲ " : "▼ ") + Math.Abs(s.Count - s.PreviousCount).ToString(CultureInfo.InvariantCulture) : "";
    public string AverageChange => Stats is { PreviousAverage: > 0 } s ? Change(s.AverageValue, s.PreviousAverage) : "";
    public bool ValueUp => Stats is { } s && s.QuotedValue >= s.PreviousValue;
    public bool CountUp => Stats is { } s && s.Count >= s.PreviousCount;
    public bool AverageUp => Stats is { } s && s.AverageValue >= s.PreviousAverage;
    public Geometry? ValueSpark => Spark(Stats?.ValueTrend.Select(v => (double)v));
    public Geometry? CountSpark => Spark(Stats?.CountTrend.Select(v => (double)v));
    public Geometry? AverageSpark => Spark(Stats?.AverageTrend.Select(v => (double)v));

    public bool HasBiggest => Stats?.Biggest is not null;
    public string BiggestValue => Stats?.Biggest is { } b ? Money(b.Value) : "";
    public string BiggestDetail => Stats?.Biggest is { } b ? $"{b.Number} · {b.Customer}" : "";

    public ObservableCollection<LeaderRow> TopCustomers { get; } = [];
    public ObservableCollection<LeaderRow> TopItems { get; } = [];
    public ObservableCollection<TeamRow> Team { get; } = [];
    public ObservableCollection<HeatCell> Heat { get; } = [];
    public bool HasCustomers => TopCustomers.Count > 0;
    public bool HasItems => TopItems.Count > 0;
    public bool HasTeam => Team.Count > 0;
    public string CustomersSubtitle => (CustomersByCount ? "by times quoted · " : "by value · ") + PeriodWord;
    public string TeamTitle => _session.IsAdmin ? "Team " + PeriodWord : "Your place " + PeriodWord;

    public bool ShowOutcomes => Stats?.Outcomes is not null;
    /// <summary>Without Won / Lost the activity map takes the whole row.</summary>
    public int ActivityColumn => ShowOutcomes ? 2 : 0;
    public int ActivitySpan => ShowOutcomes ? 1 : 3;
    public string WonPercent => Stats?.Outcomes is { } o && o.Won + o.Lost + o.Open > 0 ? $"{o.Won * 100 / (o.Won + o.Lost + o.Open)}%" : "–";
    public string WonText => Stats?.Outcomes is { } o ? $"Won {o.Won}" : "";
    public string LostText => Stats?.Outcomes is { } o ? $"Lost {o.Lost}" : "";
    public string OpenText => Stats?.Outcomes is { } o ? $"Open {o.Open}" : "";
    public string OutcomeDetail => Stats?.Outcomes is { } o ? $"Won value {Short(o.WonValue)} · {o.FollowUpsDue} follow-up{(o.FollowUpsDue == 1 ? "" : "s")} due" : "";
    public Geometry? WonArc => Arc(0, Stats?.Outcomes is { } o ? Share(o.Won, o) : 0);
    public Geometry? LostArc => Stats?.Outcomes is { } o ? Arc(Share(o.Won, o), Share(o.Lost, o)) : null;
    public Geometry? OpenArc => Stats?.Outcomes is { } o ? Arc(Share(o.Won, o) + Share(o.Lost, o), Share(o.Open, o)) : null;

    public string StreakText => Stats is { Streak: > 1 } s ? $"🔥 {s.Streak}-day streak" : "";
    public string BusiestText => Stats is { BusiestDay.Length: > 0 } s ? $"Busiest: {s.BusiestDay} · {s.BusiestHours}" : "";

    partial void OnPeriodChanged(string value)
    {
        foreach (var n in new[] { nameof(PeriodToday), nameof(PeriodWeek), nameof(PeriodMonth), nameof(PeriodFy), nameof(ValueLabel) }) OnPropertyChanged(n);
        _ = LoadStatsAsync();
    }

    partial void OnCustomersByCountChanged(bool value)
    {
        OnPropertyChanged(nameof(CustomersByValue));
        OnPropertyChanged(nameof(CustomersSubtitle));
        FillCustomers();
    }

    partial void OnStatsChanged(DashboardStatsDto? value)
    {
        FillCustomers();
        TopItems.Clear();
        var items = value?.Items ?? [];
        var maxTimes = items.Count == 0 ? 1 : Math.Max(1, items.Max(i => i.Times));
        foreach (var (item, i) in items.Select((x, i) => (x, i)))
            TopItems.Add(new LeaderRow(i + 1, item.Name, $"{item.Times}×", Money(item.Value), item.Times * 100.0 / maxTimes));
        Team.Clear();
        foreach (var t in value?.Team ?? [])
            Team.Add(new TeamRow(t.Rank, t.DisplayName, t.DisplayName.Length > 0 ? t.DisplayName[..1].ToUpperInvariant() : "?",
                $"{t.Count} quotation{(t.Count == 1 ? "" : "s")} · {Short(t.Value)}", t.Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"#{t.Rank}" }, t.Me));
        Heat.Clear();
        if (value is not null)
        {
            // 7 rows (Sun … Sat) × weeks, filled row by row for the grid.
            var days = value.Activity;
            var first = days.Count == 0 ? DateOnly.FromDateTime(DateTime.Today) : days[0].Date;
            var weeks = days.Count == 0 ? 0 : (days.Count + 6) / 7;
            var byDate = days.ToDictionary(d => d.Date, d => d.Count);
            var max = Math.Max(1, days.Count == 0 ? 1 : days.Max(d => d.Count));
            for (var row = 0; row < 7; row++)
                for (var col = 0; col < weeks; col++)
                {
                    var date = first.AddDays(col * 7 + row);
                    var known = byDate.TryGetValue(date, out var n);
                    Heat.Add(new HeatCell(date, known ? n : -1, known ? Level(n, max) : -1));
                }
            HeatColumns = weeks;
        }
        foreach (var n in new[] { nameof(Summary), nameof(ValueText), nameof(CountText), nameof(AverageText), nameof(ValueChange), nameof(CountChange),
                     nameof(AverageChange), nameof(ValueUp), nameof(CountUp), nameof(AverageUp), nameof(ValueSpark), nameof(CountSpark), nameof(AverageSpark),
                     nameof(HasBiggest), nameof(BiggestValue), nameof(BiggestDetail), nameof(HasItems), nameof(HasTeam), nameof(TeamTitle), nameof(ShowOutcomes), nameof(ActivityColumn), nameof(ActivitySpan),
                     nameof(WonPercent), nameof(WonText), nameof(LostText), nameof(OpenText), nameof(OutcomeDetail), nameof(WonArc), nameof(LostArc),
                     nameof(OpenArc), nameof(StreakText), nameof(BusiestText), nameof(ValueLabel), nameof(HeatColumns) })
            OnPropertyChanged(n);
    }

    public int HeatColumns { get; private set; } = 17;

    private void FillCustomers()
    {
        TopCustomers.Clear();
        var list = (CustomersByCount ? Stats?.CustomersByCount : Stats?.CustomersByValue) ?? [];
        var max = list.Count == 0 ? 1 : (double)Math.Max(1, CustomersByCount ? list.Max(c => c.Count) : list.Max(c => c.Value));
        foreach (var (c, i) in list.Select((x, i) => (x, i)))
        {
            var avg = c.Count == 0 ? 0 : c.Value / c.Count;
            TopCustomers.Add(new LeaderRow(i + 1, c.Name, CustomersByCount ? $"{c.Count}×" : Money(c.Value),
                $"{c.Count} quotation{(c.Count == 1 ? "" : "s")} · avg {Money(Math.Round(avg, 0))}" + (CustomersByCount ? $" · {Short(c.Value)}" : ""),
                (CustomersByCount ? c.Count : (double)c.Value) * 100 / max));
        }
        OnPropertyChanged(nameof(HasCustomers));
    }

    private Task LoadStatsAsync() => RunAsync(async () =>
    {
        try { Stats = await _session.Api.DashboardStatsAsync(Period); }
        catch (ApiClient.ApiException) { Stats = null; } // an older server without the numbers: the rest still shows
    });

    private static int Level(int n, int max) => n == 0 ? 0 : Math.Clamp((int)Math.Ceiling(n * 5.0 / max), 1, 5);

    private static double Share(int part, DashboardOutcomesDto o) => o.Won + o.Lost + o.Open == 0 ? 0 : (double)part / (o.Won + o.Lost + o.Open);

    /// <summary>An arc of the Won / Lost ring (from and length as parts of the whole circle; radius 46 around 60,60).</summary>
    private static Geometry? Arc(double from, double length)
    {
        if (length <= 0) return null;
        if (length >= 0.999) length = 0.999;
        static (double X, double Y) P(double part) => (60 + 46 * Math.Cos((part * 360 - 90) * Math.PI / 180), 60 + 46 * Math.Sin((part * 360 - 90) * Math.PI / 180));
        var (x1, y1) = P(from);
        var (x2, y2) = P(from + length);
        var large = length > 0.5 ? 1 : 0;
        return Geometry.Parse(string.Create(CultureInfo.InvariantCulture, $"M{x1:0.##},{y1:0.##} A46,46 0 {large} 1 {x2:0.##},{y2:0.##}"));
    }

    /// <summary>A small trend line (96 × 30) of the period's 12 slices.</summary>
    private static Geometry? Spark(IEnumerable<double>? values)
    {
        var v = values?.ToList();
        if (v is null || v.Count < 2 || v.All(x => x == 0)) return null;
        var max = v.Max();
        var step = 96.0 / (v.Count - 1);
        var points = v.Select((x, i) => string.Create(CultureInfo.InvariantCulture, $"{i * step:0.#},{28 - x / max * 26:0.#}"));
        return Geometry.Parse("M" + string.Join(" L", points));
    }

    private static string Change(decimal now, decimal before)
    {
        var pct = before == 0 ? 0 : (int)Math.Round((now - before) * 100 / before);
        return (pct >= 0 ? "▲ " : "▼ ") + Math.Abs(pct).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private static string Money(decimal v) => "₹ " + v.ToString("#,##,##0", Formatting.India);

    /// <summary>₹ 28.4 L / ₹ 1.2 Cr / ₹ 45,810 (words: "₹ 28.4 lakh").</summary>
    internal static string Short(decimal v, bool words = false) => v switch
    {
        >= 1_00_00_000 => "₹ " + (v / 1_00_00_000).ToString("0.#", CultureInfo.InvariantCulture) + (words ? " crore" : " Cr"),
        >= 1_00_000 => "₹ " + (v / 1_00_000).ToString("0.#", CultureInfo.InvariantCulture) + (words ? " lakh" : " L"),
        _ => Money(v),
    };

    private readonly AppSession _session;
    private readonly Action<string> _navigate;

    [ObservableProperty] private DashboardDto? _data;

    /// <summary>Administrators: old backup, copy folder missing, low disk, no Tally sync for a day.</summary>
    public ObservableCollection<HealthWarningDto> Warnings { get; } = [];
    public bool HasWarnings => Warnings.Count > 0;
    public bool ShowFollowUp => Data?.TrackOutcomes == true;

    public DashboardViewModel(AppSession session, Action<string> navigate)
    {
        _session = session;
        _navigate = navigate;
        _ = LoadAsync();
    }

    public string TallyText => Data?.Status.TallyConnected == true ? "Connected" : "Disconnected";
    public string GmailText => Data?.Status.GmailConnected == true ? "Connected" : "Not connected";
    public string AiText => Data?.Status.AiConfigured == true ? "Set up" : "Not set up";
    public bool TallyOk => Data?.Status.TallyConnected == true;
    public bool GmailOk => Data?.Status.GmailConnected == true;
    public bool AiOk => Data?.Status.AiConfigured == true;
    public string LastSyncText => Formatting.LocalDateTime(Data?.Status.LastSuccessfulSyncUtc) ?? "Never";

    partial void OnDataChanged(DashboardDto? value)
    {
        OnPropertyChanged(nameof(TallyText));
        OnPropertyChanged(nameof(GmailText));
        OnPropertyChanged(nameof(AiText));
        OnPropertyChanged(nameof(TallyOk));
        OnPropertyChanged(nameof(GmailOk));
        OnPropertyChanged(nameof(AiOk));
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(ShowFollowUp));
    }

    public void OnReturnedTo() => _ = LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Data = await _session.Api.DashboardAsync();
        _ = LoadStatsAsync();
        if (!_session.IsAdmin) return;
        List<HealthWarningDto> list;
        try { list = await _session.Api.HealthWarningsAsync(); }
        catch { return; } // an older server without the check: just show nothing
        Warnings.Clear();
        foreach (var w in list) Warnings.Add(w);
        OnPropertyChanged(nameof(HasWarnings));
    });

    [RelayCommand]
    private void Go(string key) => _navigate(key);
}

/// <summary>Round 41 (DB-1): one row of a dashboard top list (customers or items).</summary>
public sealed record LeaderRow(int Rank, string Name, string Value, string Detail, double Bar)
{
    public bool Gold => Rank == 1;
    public bool Silver => Rank == 2;
    public bool Bronze => Rank == 3;
}

/// <summary>Round 41 (DB-1): one person in "Team this month".</summary>
public sealed record TeamRow(int Rank, string Name, string Initial, string Detail, string Medal, bool Me);

/// <summary>Round 41 (DB-1): one day of the activity map (Level −1 = outside the range, 0 = none … 5 = busiest).</summary>
public sealed record HeatCell(DateOnly Date, int Count, int Level)
{
    public string Tip => Level < 0 ? "" : $"{Date:ddd d MMM}: {Count} quotation{(Count == 1 ? "" : "s")}";
    public bool L0 => Level == 0;
    public bool L1 => Level == 1;
    public bool L2 => Level == 2;
    public bool L3 => Level == 3;
    public bool L4 => Level == 4;
    public bool L5 => Level == 5;
    public bool Outside => Level < 0;
}
