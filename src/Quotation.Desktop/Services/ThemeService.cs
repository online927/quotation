using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>
/// Each user's screens: light / dark or a colour theme, text size, spacing and animations (My Settings > Appearance).
/// PDFs are always printed white.
/// </summary>
public static class ThemeService
{
    private static string? _theme;
    private static bool _reduceMotion;

    public static void Apply(string? theme)
    {
        if (Application.Current is not { } app) return;
        _theme = theme;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.System => ThemeVariant.Default,
            _ when AppTheme.IsDarkTheme(theme) => ThemeVariant.Dark,
            _ => ThemeVariant.Light,
        };
        ColourThemes.Apply(theme);
        ApplyShadow(app);
    }

    /// <summary>The screens are dark now (chosen, or Windows is dark when following Windows).</summary>
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Raised when the text size changes (the main window zooms its content).</summary>
    public static event Action<double>? ZoomChanged;
    public static double Zoom { get; private set; } = 1;

    /// <summary>Text size (Normal / Large), spacing (Comfortable / Compact) and animations on or off.</summary>
    public static void ApplyDisplay(string? textSize, string? spacing, bool reduceMotion)
    {
        if (Application.Current is not { } app) return;
        var zoom = textSize == AppDisplay.Large ? 1.15 : 1.0;
        if (Math.Abs(zoom - Zoom) > 0.001)
        {
            Zoom = zoom;
            ZoomChanged?.Invoke(zoom);
        }
        var compact = spacing == AppDisplay.Compact;
        app.Resources["CardPadding"] = new Thickness(compact ? 12 : 18);
        app.Resources["SectionSpacing"] = compact ? 6.0 : 10.0;
        app.Resources["InputMinHeight"] = compact ? 24.0 : 28.0;
        app.Resources["RowPadding"] = compact ? new Thickness(12, 3) : new Thickness(12, 6);
        SetMotion(app, !reduceMotion);
        _reduceMotion = reduceMotion;
        ApplyShadow(app);
    }

    public static bool AnimationsOn => !_reduceMotion;

    private static Styles.MotionStyles? _motion;
    private static Application? _motionApp;

    /// <summary>Animations are a set of styles added to (or removed from) the app.</summary>
    private static void SetMotion(Application app, bool on)
    {
        if (_motion is null || _motionApp != app)
        {
            _motion = new Styles.MotionStyles();
            _motionApp = app;
        }
        var present = app.Styles.Contains(_motion);
        if (on && !present) app.Styles.Add(_motion);
        if (!on && present) app.Styles.Remove(_motion);
    }

    /// <summary>Animations on at start (before anyone signs in).</summary>
    public static void Initialise()
    {
        if (Application.Current is { } app) SetMotion(app, true);
    }

    /// <summary>Glow shadows of a theme, unless animations are reduced (then the standard small shadow).</summary>
    private static void ApplyShadow(Application app) =>
        app.Resources["CardShadow"] = BoxShadows.Parse(ColourThemes.GlowShadow is { } glow && !_reduceMotion ? glow : "0 1 3 0 #0F000000");
}
