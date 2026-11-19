namespace Quotation.Contracts;

public enum Festival
{
    None,
    Eid,
    Diwali,
}

/// <summary>
/// The festival days on which the app wears its festival look (Settings → Quotation → Festival looks, administrator).
/// Eid follows the moon (the day can move by one day), so the look is on from the day before to two days after; Diwali
/// from Dhanteras-time (two days before) to the day after.
/// </summary>
public static class FestivalCalendar
{
    /// <summary>Eid al-Fitr and Eid al-Adha (Bakrid) as expected in India.</summary>
    public static readonly IReadOnlyList<DateOnly> EidDays =
    [
        new(2026, 3, 21), new(2026, 5, 27),
        new(2027, 3, 10), new(2027, 5, 17),
        new(2028, 2, 27), new(2028, 5, 5),
        new(2029, 2, 15), new(2029, 4, 24),
        new(2030, 2, 5), new(2030, 4, 14),
        new(2031, 1, 25), new(2031, 4, 3),
        new(2032, 1, 14), new(2032, 3, 22),
    ];

    /// <summary>Diwali (Lakshmi Puja).</summary>
    public static readonly IReadOnlyList<DateOnly> DiwaliDays =
    [
        new(2026, 11, 8), new(2027, 10, 29), new(2028, 10, 17), new(2029, 11, 5), new(2030, 10, 26), new(2031, 11, 14),
        new(2032, 11, 2),
    ];

    public static Festival On(DateOnly date)
    {
        foreach (var eid in EidDays)
            if (date >= eid.AddDays(-1) && date <= eid.AddDays(2)) return Festival.Eid;
        foreach (var diwali in DiwaliDays)
            if (date >= diwali.AddDays(-2) && date <= diwali.AddDays(1)) return Festival.Diwali;
        return Festival.None;
    }

    /// <summary>The greeting over the dashboard on the festival days.</summary>
    public static string Greeting(Festival festival) => festival switch
    {
        Festival.Eid => "Eid Mubarak!",
        Festival.Diwali => "Happy Diwali!",
        _ => "",
    };
}
