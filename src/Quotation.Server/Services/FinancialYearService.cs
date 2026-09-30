using Quotation.Core.Domain;

namespace Quotation.Server.Services;

/// <summary>
/// Determines the active financial year. Priority: administrator override → value detected
/// from Tally (current period) → last value detected from Tally (persisted) → today's date.
/// </summary>
public sealed class FinancialYearService(SettingsService settings, RuntimeStatus runtime, TimeProvider clock)
{
    public const string LastDetectedKey = "state:tally.activefy";

    public sealed record ActiveFinancialYear(FinancialYear Year, string Source);

    public ActiveFinancialYear GetActive()
    {
        var startMonth = settings.Quotation.FinancialYearStartMonth;
        var overrideYear = settings.Tally.ActiveFinancialYearOverride;
        if (overrideYear is not null) return new(new FinancialYear(overrideYear.Value, startMonth), "Override");

        if (runtime.TallyActiveFinancialYearStart is { } detected) return new(new FinancialYear(detected, startMonth), "Tally");

        var persisted = settings.Get<PersistedFy>(LastDetectedKey);
        if (persisted.StartYear > 0) return new(new FinancialYear(persisted.StartYear, startMonth), "Tally (last known)");

        return new(FinancialYear.For(Today(), startMonth), "System date");
    }

    public async Task RememberDetectedAsync(int startYear, CancellationToken ct)
    {
        if (settings.Get<PersistedFy>(LastDetectedKey).StartYear == startYear) return;
        await settings.SaveAsync(LastDetectedKey, new PersistedFy { StartYear = startYear }, "system", ct);
    }

    public DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public sealed class PersistedFy
    {
        public int StartYear { get; set; }
    }
}
