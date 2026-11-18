using Avalonia;
using Avalonia.Media;

namespace Quotation.Desktop.Services;

/// <summary>
/// How selected text looks in the typing boxes (My Settings, this PC). The old look (solid blue with the letters left
/// dark) hid the letters; each choice here keeps them readable. Set as two app resources that every typing box uses.
/// </summary>
public static class TextSelectionLook
{
    public const string SoftBlue = "SoftBlue", SolidBlue = "SolidBlue", Marker = "Marker", ThemeColour = "ThemeColour";
    public static readonly string[] All = [SoftBlue, SolidBlue, Marker, ThemeColour];

    public static string Normalize(string? look) => All.FirstOrDefault(a => a.Equals(look, StringComparison.OrdinalIgnoreCase)) ?? SoftBlue;

    private static string _look = SoftBlue;
    private static bool _listening;

    /// <summary>The look now on the screens.</summary>
    public static string Current => _look;

    /// <summary>The highlight and the letters (null: the letters keep their own colour, for "theme colour").</summary>
    public static (Color Back, Color? Letters) Colours(string? look, Color accent) => Normalize(look) switch
    {
        SolidBlue => (Color.Parse("#1A73E8"), Colors.White),
        Marker => (Color.Parse("#FFE58A"), Color.Parse("#111827")),
        ThemeColour => (Color.FromArgb(0x55, accent.R, accent.G, accent.B), null),
        _ => (Color.Parse("#B3D7FF"), Color.Parse("#111827")),
    };

    public static void Apply(string? look)
    {
        _look = Normalize(look);
        if (!_listening)
        {
            _listening = true;
            ThemeService.ColoursChanged += Refresh; // "theme colour" follows the theme
        }
        Refresh();
    }

    /// <summary>The theme's main colour (for "theme colour").</summary>
    public static Color Accent()
    {
        var app = Application.Current;
        return app is not null && app.Resources.TryGetResource("AccentBrush", app.ActualThemeVariant, out var a) && a is ISolidColorBrush b
            ? b.Color
            : Color.Parse("#0A5FD0");
    }

    private static void Refresh()
    {
        if (Application.Current is not { } app) return;
        var (back, letters) = Colours(_look, Accent());
        app.Resources["TextSelectionBrush"] = new SolidColorBrush(back);
        app.Resources["TextSelectionForegroundBrush"] = letters is { } l ? new SolidColorBrush(l) : null;
    }
}
