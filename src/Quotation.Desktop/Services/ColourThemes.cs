using Avalonia;
using Avalonia.Media;
using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>
/// Colour themes (My Settings > Appearance). Sunset, Graphite and Neon Night change the sidebar,
/// highlights, links, icons and page tint (action buttons keep their colours); Frost and the four Glass themes change
/// everything (buttons, see-through cards, corners, shadows). Default Light / Dark use the standard colours.
/// </summary>
public static class ColourThemes
{
    /// <summary>Colours of a theme. "A→B" (or "A→B→C") is a top-to-bottom gradient.</summary>
    private sealed record Palette(string Accent, string AccentSoft, string Sidebar, string SidebarHover, string SidebarSelected,
        string Page, string ListSelected, string ListHover);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        // Graphite: a graphite-grey page with white boxes and a dark graphite sidebar.
        [AppTheme.Graphite] = new("#3A3F4B", "#E4E6EA", "#1F2125", "#2E3137", "#4A505C", "#D4D7DC→#C6CAD0", "#C3C7CE", "#E9EBEE"),
        [AppTheme.Sunset] = new("#E5345E", "#FDE8EE", "#B8243F→#E2483A→#F07B2A", "#22000000", "#40FFFFFF", "#FFF6F1→#FDEEF3", "#F9CBD7", "#FDF0F3"),
        [AppTheme.NeonNight] = new("#00D8F5", "#06303A", "#07051A→#140B3A", "#1F1650", "#00B8D9", "#0B0A1C→#121035", "#0D4A57", "#13213A"),
    };

    /// <summary>Frost: icy glass look that also changes the buttons, corners and shadows.</summary>
    private static readonly Dictionary<string, string> FrostBrushes = new()
    {
        ["PageBrush"] = "#DDEFFF→#F4FAFF→#E3E9FF", ["CardBrush"] = "#B8FFFFFF", ["PopupBrush"] = "#FAFCFF", ["TopBarBrush"] = "#99FFFFFF",
        ["SidebarBrush"] = "#C2DAF0→#A9C4E8", ["SidebarBorderBrush"] = "#A9C4E8", ["SidebarSelectedBrush"] = "#FFFFFF", ["SidebarHoverBrush"] = "#66FFFFFF",
        ["SidebarTextBrush"] = "#173A5E", ["SidebarMutedBrush"] = "#4E6E90", ["SidebarSelectedTextBrush"] = "#0B5CAD",
        ["AccentBrush"] = "#1E7FD6", ["AccentSoftBrush"] = "#E1F0FF", ["ActionBrush"] = "#4FACFE→#00C6FB", ["PrimaryHoverBrush"] = "#62B6FE→#22CFFB",
        ["SaveBrush"] = "#43CEA2→#2E9BD8", ["SaveHoverBrush"] = "#55D6AD→#40A6DD", ["HairlineBrush"] = "#80FFFFFF",
        ["ListSelectedBrush"] = "#CFE6FF", ["ListSelectedHoverBrush"] = "#CFE6FF", ["ListSelectedBarBrush"] = "#1E7FD6", ["ListHoverBrush"] = "#E6F2FF",
        ["TextControlBorderBrushFocused"] = "#1E7FD6", ["LinkHoverBrush"] = "#1E7FD6", ["TileHoverBorderBrush"] = "#CFE6FF",
    };

    private const string FrostShadow = "0 8 30 0 #1A1E7FD6";

    /// <summary>
    /// Glass themes: a wallpaper in one simple colour behind see-through panels with a bright edge, round corners and
    /// blue / green buttons. Flat colours only (no live blur), so they are as light as the other themes.
    /// </summary>
    private sealed record Glass(string Wallpaper, string Accent, string AccentSoft, string Selected, bool Dark);

    private static readonly Dictionary<string, Glass> GlassThemes = new()
    {
        [AppTheme.GlassBlue] = new("#6F9FE6→#A4C4F0→#D6E5F9", "#0A5FD0", "#DCE9FB", "#C7DCF8", false),
        [AppTheme.GlassGreen] = new("#6DBF97→#A5D9BD→#D8EFE2", "#0E7A4F", "#DBF1E6", "#C4E6D3", false),
        [AppTheme.GlassMaroon] = new("#B97683→#D6A6AF→#EFD8DC", "#8E1B2E", "#F6E1E5", "#EDC9D0", false),
        [AppTheme.GlassBlack] = new("#050507→#17171C→#2A2A31", "#0A84FF", "#1A2A40", "#2C3E57", true),
    };

    private static Dictionary<string, string> GlassBrushes(Glass g) => g.Dark
        ? new()
        {
            ["PageBrush"] = g.Wallpaper, ["CardBrush"] = "#24FFFFFF", ["PopupBrush"] = "#2A2A30", ["TopBarBrush"] = "#1AFFFFFF",
            ["SidebarBrush"] = "#14FFFFFF", ["SidebarBorderBrush"] = "#26FFFFFF", ["SidebarSelectedBrush"] = "#38FFFFFF", ["SidebarHoverBrush"] = "#1AFFFFFF",
            ["SidebarTextBrush"] = "#F2F2F7", ["SidebarMutedBrush"] = "#A1A1AA", ["SidebarSelectedTextBrush"] = "#FFFFFF",
            ["AccentBrush"] = g.Accent, ["AccentSoftBrush"] = g.AccentSoft, ["ActionBrush"] = "#0A84FF", ["PrimaryHoverBrush"] = "#3D9BFF",
            ["SaveBrush"] = "#30B350", ["SaveHoverBrush"] = "#3EC25E", ["HairlineBrush"] = "#33FFFFFF", ["PanelBorderBrush"] = "#40FFFFFF",
            ["SubtleFillBrush"] = "#14FFFFFF", ["FillBrush"] = "#1FFFFFFF", ["MutedBrush"] = "#A9A9B2",
            ["ListSelectedBrush"] = g.Selected, ["ListSelectedHoverBrush"] = g.Selected, ["ListSelectedBarBrush"] = g.Accent, ["ListHoverBrush"] = "#14FFFFFF",
            ["TextControlBorderBrushFocused"] = g.Accent, ["LinkHoverBrush"] = g.Accent, ["TileHoverBorderBrush"] = "#66FFFFFF",
        }
        : new()
        {
            ["PageBrush"] = g.Wallpaper, ["CardBrush"] = "#A6FFFFFF", ["PopupBrush"] = "#FAFBFC", ["TopBarBrush"] = "#73FFFFFF",
            ["SidebarBrush"] = "#4DFFFFFF", ["SidebarBorderBrush"] = "#80FFFFFF", ["SidebarSelectedBrush"] = "#E6FFFFFF", ["SidebarHoverBrush"] = "#40FFFFFF",
            ["SidebarTextBrush"] = "#1C1C1E", ["SidebarMutedBrush"] = "#3C3C48", ["SidebarSelectedTextBrush"] = g.Accent,
            ["AccentBrush"] = g.Accent, ["AccentSoftBrush"] = g.AccentSoft, ["ActionBrush"] = "#007AFF", ["PrimaryHoverBrush"] = "#1A88FF",
            ["SaveBrush"] = "#34C759", ["SaveHoverBrush"] = "#48D06A", ["HairlineBrush"] = "#CCFFFFFF", ["PanelBorderBrush"] = "#B3FFFFFF",
            ["SubtleFillBrush"] = "#80FFFFFF", ["FillBrush"] = "#99FFFFFF", ["MutedBrush"] = "#4A4A55",
            ["ListSelectedBrush"] = g.Selected, ["ListSelectedHoverBrush"] = g.Selected, ["ListSelectedBarBrush"] = g.Accent, ["ListHoverBrush"] = "#66FFFFFF",
            ["TextControlBorderBrushFocused"] = g.Accent, ["LinkHoverBrush"] = g.Accent, ["TileHoverBorderBrush"] = "#FFFFFF",
        };

    private const string GlassShadow = "0 10 30 0 #262A1F6B";

    /// <summary>Applies the colours of a theme (light or dark itself is chosen by <see cref="ThemeService"/>).</summary>
    public static void Apply(string? theme)
    {
        if (Application.Current is not { } app) return;
        // Back to the standard colours and shapes first.
        foreach (var key in AllKeys) app.Resources.Remove(key);
        RestoreShapes(app);
        GlowShadow = null;

        if (theme is not null && GlassThemes.TryGetValue(theme, out var glass))
        {
            foreach (var (key, colour) in GlassBrushes(glass)) app.Resources[key] = BrushOf(colour);
            SetShape(app, "CardRadius", new CornerRadius(22));
            SetShape(app, "CardBottomRadius", new CornerRadius(0, 0, 22, 22));
            SetShape(app, "ButtonRadius", new CornerRadius(18));
            SetShape(app, "ControlCornerRadius", new CornerRadius(12));
            GlowShadow = GlassShadow;
            return;
        }
        if (theme == AppTheme.Frost)
        {
            foreach (var (key, colour) in FrostBrushes) app.Resources[key] = BrushOf(colour);
            SetShape(app, "CardRadius", new CornerRadius(16));
            SetShape(app, "CardBottomRadius", new CornerRadius(0, 0, 16, 16));
            SetShape(app, "ButtonRadius", new CornerRadius(12));
            SetShape(app, "ControlCornerRadius", new CornerRadius(10));
            GlowShadow = FrostShadow;
            return;
        }
        if (theme is null || !Palettes.TryGetValue(theme, out var p)) return;
        void Set(string key, string colour) => app.Resources[key] = BrushOf(colour);
        Set("AccentBrush", p.Accent);
        Set("AccentSoftBrush", p.AccentSoft);
        Set("SidebarBrush", p.Sidebar);
        Set("SidebarBorderBrush", p.Sidebar.Split('→')[^1]);
        Set("SidebarHoverBrush", p.SidebarHover);
        Set("SidebarSelectedBrush", p.SidebarSelected);
        Set("PageBrush", p.Page);
        Set("ListSelectedBrush", p.ListSelected);
        Set("ListSelectedHoverBrush", p.ListSelected);
        Set("ListSelectedBarBrush", p.Accent);
        Set("ListHoverBrush", p.ListHover);
        Set("TextControlBorderBrushFocused", p.Accent);
        Set("LinkHoverBrush", p.Accent);
        Set("TileHoverBorderBrush", p.ListSelected);
        if (theme is AppTheme.NeonNight) GlowShadow = $"0 0 18 0 #33{p.Accent[1..]}";
    }

    /// <summary>The glow around cards of the current theme (null = the standard small shadow). Off with "Reduce animations".</summary>
    public static string? GlowShadow { get; private set; }

    /// <summary>"#RRGGBB" / "#AARRGGBB", or a gradient "#A→#B(→#C)".</summary>
    public static IBrush BrushOf(string colour)
    {
        var parts = colour.Split('→');
        if (parts.Length == 1) return new SolidColorBrush(Color.Parse(colour));
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.35, 1, RelativeUnit.Relative),
        };
        for (var i = 0; i < parts.Length; i++) brush.GradientStops.Add(new GradientStop(Color.Parse(parts[i]), (double)i / (parts.Length - 1)));
        return brush;
    }

    private static readonly string[] AllKeys = FrostBrushes.Keys.Concat(GlassBrushes(GlassThemes[AppTheme.GlassBlue]).Keys).Concat(
    [
        "AccentBrush", "AccentSoftBrush", "SidebarBrush", "SidebarBorderBrush", "SidebarHoverBrush", "SidebarSelectedBrush", "PageBrush",
        "ListSelectedBrush", "ListSelectedHoverBrush", "ListSelectedBarBrush", "ListHoverBrush", "TextControlBorderBrushFocused",
        "LinkHoverBrush", "TileHoverBorderBrush",
    ]).Distinct().ToArray();

    // Shapes (corners) live at the top of the app resources: remember the standard values to put them back.
    private static readonly string[] ShapeKeys = ["CardRadius", "CardBottomRadius", "ButtonRadius", "ControlCornerRadius"];
    private static readonly Dictionary<string, object?> Standard = [];

    private static void SetShape(Application app, string key, object value)
    {
        if (!Standard.ContainsKey(key) && app.Resources.TryGetValue(key, out var original)) Standard[key] = original;
        app.Resources[key] = value;
    }

    private static void RestoreShapes(Application app)
    {
        foreach (var key in ShapeKeys)
            if (Standard.TryGetValue(key, out var value)) app.Resources[key] = value;
    }
}
