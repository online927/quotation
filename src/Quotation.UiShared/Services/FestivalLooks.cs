using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>
/// The festival looks (Eid, Diwali): worn by themselves on the festival days instead of each user's theme while the
/// administrator's switch is on (Settings → Quotation → Festival looks), then each user's own theme again. Not themes a
/// user picks, and never saved as anyone's theme.
/// </summary>
public static class FestivalLooks
{
    public const string Eid = "FestivalEid";
    public const string Diwali = "FestivalDiwali";

    public static bool IsFestivalLook(string? theme) => theme is Eid or Diwali;

    public static string? For(Festival festival) => festival switch
    {
        Festival.Eid => Eid,
        Festival.Diwali => Diwali,
        _ => null,
    };

    /// <summary>Today's festival (none while the administrator's switch is off). Tests set <see cref="Today"/>.</summary>
    public static Festival Now(bool switchedOn) => switchedOn ? FestivalCalendar.On(Today()) : Festival.None;

    public static Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Puts the festival look on (or takes it off) for today, from the administrator's switch.</summary>
    public static void Refresh(bool switchedOn) => ThemeService.SetFestival(For(Now(switchedOn)));

    /// <summary>"Eid Mubarak!  ·  Today at a glance" on the festival days, else "Today at a glance".</summary>
    public static string GlanceTitle => ThemeService.Festival switch
    {
        Eid => FestivalCalendar.Greeting(Festival.Eid) + "  ·  Today at a glance",
        Diwali => FestivalCalendar.Greeting(Festival.Diwali) + "  ·  Today at a glance",
        _ => "Today at a glance",
    };
}
