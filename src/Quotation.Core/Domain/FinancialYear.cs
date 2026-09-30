using System.Globalization;

namespace Quotation.Core.Domain;

/// <summary>
/// A financial year identified by its start year. The Indian financial year runs
/// 1-Apr to 31-Mar (start month 4), but the start month is configurable.
/// </summary>
public readonly record struct FinancialYear(int StartYear, int StartMonth = 4) : IComparable<FinancialYear>
{
    public DateOnly StartDate => new(StartYear, StartMonth, 1);
    public DateOnly EndDate => StartDate.AddYears(1).AddDays(-1);
    public int EndYear => EndDate.Year;

    /// <summary>"2025-26".</summary>
    public string Label => StartMonth == 1
        ? StartYear.ToString(CultureInfo.InvariantCulture)
        : $"{StartYear}-{EndYear % 100:00}";

    /// <summary>"2526" — the form used in quotation numbers such as TSQ2526-3247.</summary>
    public string ShortCode => StartMonth == 1
        ? (StartYear % 100).ToString("00", CultureInfo.InvariantCulture)
        : $"{StartYear % 100:00}{EndYear % 100:00}";

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    public FinancialYear Next() => this with { StartYear = StartYear + 1 };
    public FinancialYear Previous() => this with { StartYear = StartYear - 1 };

    public static FinancialYear For(DateOnly date, int startMonth = 4)
    {
        if (startMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(startMonth));
        var start = date.Month >= startMonth ? date.Year : date.Year - 1;
        return new FinancialYear(start, startMonth);
    }

    public int CompareTo(FinancialYear other) => StartDate.CompareTo(other.StartDate);

    public override string ToString() => Label;
}
