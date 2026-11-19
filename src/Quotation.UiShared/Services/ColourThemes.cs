using Avalonia;
using Avalonia.Media;
using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>
/// Colour themes (My Settings > Appearance). Graphite changes the sidebar, highlights, links, icons and page tint (action
/// buttons keep their colours); the Glass themes and the drawn ones (Parchment, Forest &amp; Gold, Hero Light, Ocean, Midnight
/// Stars) change everything (buttons, cards, corners, shadows). Default Light / Dark use the standard colours.
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
    };


    /// <summary>
    /// Glass themes: a wallpaper in one simple colour behind see-through panels with a bright edge, round corners and
    /// blue / green buttons. Flat colours only (no live blur), so they are as light as the other themes.
    /// </summary>
    private sealed record Glass(string Wallpaper, string Accent, string AccentSoft, string Selected, bool Dark,
        Dictionary<string, string>? Extra = null);

    private static readonly Dictionary<string, Glass> GlassThemes = new()
    {
        [AppTheme.GlassBlue] = new("#6F9FE6→#A4C4F0→#D6E5F9", "#0A5FD0", "#DCE9FB", "#C7DCF8", false),
        // Grass Green: the green glass look with white clouds, a pale sun, soft hills and grass at the foot of the pages.
        [AppTheme.GlassGreen] = new("#6DBF97→#A5D9BD→#D8EFE2", "#0E7A4F", "#DBF1E6", "#C4E6D3", false,
            new()
            {
                ["SkyArtBrush"] = "#B3FFFFFF", ["SunArtBrush"] = "#8CFFE27A", ["FarArtBrush"] = "#4D3E9E6B",
                ["NearArtBrush"] = "#A62E8B57", ["SideArtBrush"] = "#732E8B57", ["PictureScrimBrush"] = "#B3D8EFE2",
            }),
        // Maroon (B1 "Maroon Frame"): the top bar joins the maroon menu; a calm blush page with two soft rose curves.
        [AppTheme.GlassMaroon] = new("#F6E1E5", "#8E1B2E", "#F6E1E5", "#EDC9D0", false,
            new()
            {
                ["SidebarBrush"] = "#5E1024→#8E2238", ["SidebarBorderBrush"] = "#5E1024", ["SidebarTextBrush"] = "#FFFFFF",
                ["SidebarMutedBrush"] = "#E8BCC5", ["SidebarHoverBrush"] = "#1FFFFFFF", ["SidebarSelectedBrush"] = "#33FFFFFF",
                ["SidebarSelectedTextBrush"] = "#FFFFFF", ["TopBarBrush"] = "#5E1024→#8E2238",
                ["TopBarTextBrush"] = "#FFFFFF", ["TopBarMutedBrush"] = "#E8BCC5", ["TopBarFieldBrush"] = "#29FFFFFF",
                ["BadgeBrush"] = "#FFFFFF", ["BadgeTextBrush"] = "#8E1B2E", ["HeadingBrush"] = "#5E1024",
                ["SkyArtBrush"] = "#CCEDC9D0", ["FarArtBrush"] = "#B3EDC9D0", ["NearArtBrush"] = "#00000000",
                ["SunArtBrush"] = "#00000000", ["SideArtBrush"] = "#00000000",
                // Solid boxes with a soft maroon shade (the text boxes inside stay white)
                ["CardBrush"] = "#FAEDEF", ["PopupBrush"] = "#FFFFFF", ["HairlineBrush"] = "#E8C7CE", ["SubtleFillBrush"] = "#F5E1E5",
                ["FillBrush"] = "#EFD6DB", ["ListHoverBrush"] = "#F5E1E5",
            }),
        // Parchment: aged paper, a deep forest-green menu, antique gold and dark ink.
        [AppTheme.Parchment] = new("#F4EAD0→#E9D8AE→#D8BF85", "#8B6B1E", "#F3E6C3", "#EEDDB0", false,
            new()
            {
                ["SidebarBrush"] = "#1E2B1B→#2F3F26", ["SidebarBorderBrush"] = "#1E2B1B", ["SidebarTextBrush"] = "#F2E6C8",
                ["SidebarMutedBrush"] = "#C8B583", ["SidebarHoverBrush"] = "#1FFFFFFF", ["SidebarSelectedBrush"] = "#C9A44A",
                ["SidebarSelectedTextBrush"] = "#1E2B1B", ["TopBarBrush"] = "#F6ECD4", ["CardBrush"] = "#FFFAEC", ["PopupBrush"] = "#FFFAEC",
                ["HairlineBrush"] = "#E3D2A8", ["PanelBorderBrush"] = "#D9C493", ["SubtleFillBrush"] = "#FAF1DA", ["FillBrush"] = "#F1E3BF",
                ["MutedBrush"] = "#6B5A3A", ["TextBrush"] = "#2A2114", ["ListHoverBrush"] = "#F7EDD3", ["ZebraBrush"] = "#128B6B1E",
                ["ActionBrush"] = "#7A5A12", ["PrimaryHoverBrush"] = "#8F6C1C", ["SaveBrush"] = "#3F5F2A", ["SaveHoverBrush"] = "#4D7334",
                ["PrimaryPressedBrush"] = "#664A0E", ["PrimaryDisabledBrush"] = "#D8C696",
                ["PageArtFarBrush"] = "#2E8B6B1E", ["PageArtNearBrush"] = "#473F5F2A", ["SidebarArtBrush"] = "#26C9A44A",
                ["PictureScrimBrush"] = "#B3F4EAD0",
            }),
        // Hero Light: navy menu, white boxes, bold red highlights.
        [AppTheme.HeroLight] = new("#EEF2F8→#E2E9F4→#D3DDEE", "#C8102E", "#FBE3E7", "#F6D0D7", false,
            new()
            {
                ["SidebarBrush"] = "#0B1F4A→#163572", ["SidebarBorderBrush"] = "#0B1F4A", ["SidebarTextBrush"] = "#FFFFFF",
                ["SidebarMutedBrush"] = "#AFC2E6", ["SidebarHoverBrush"] = "#1FFFFFFF", ["SidebarSelectedBrush"] = "#C8102E",
                ["SidebarSelectedTextBrush"] = "#FFFFFF", ["TopBarBrush"] = "#FFFFFF", ["CardBrush"] = "#FFFFFF", ["PopupBrush"] = "#FFFFFF",
                ["HairlineBrush"] = "#DCE3EF", ["SubtleFillBrush"] = "#F4F7FC", ["FillBrush"] = "#E9EEF7", ["MutedBrush"] = "#4A5670",
                ["TextBrush"] = "#0E1A33", ["ActionBrush"] = "#1D3A8A", ["PrimaryHoverBrush"] = "#2A4BA6",
                ["PrimaryPressedBrush"] = "#162D6B", ["PrimaryDisabledBrush"] = "#B9C6E2",
                // Epic background: rays of light from the top right and a city skyline (drawn, no pictures)
                ["RaysBrush"] = "#14FFFFFF", ["CityFarBrush"] = "#1F1D3A8A", ["CityNearBrush"] = "#381D3A8A",
                ["SidebarCityBrush"] = "#1FFFFFFF", ["PageBrush"] = "#F4F7FC→#DCE5F3→#B9C9E6",
            }),
        // Forest & Gold: dark forest-green boxes, gold accents and warm light text.
        [AppTheme.ForestGold] = new("#0E1A12→#16261A→#1F3322", "#D4AF37", "#2E3A22", "#3A4A2A", true,
            new()
            {
                ["CardBrush"] = "#1C2B1F", ["PopupBrush"] = "#1A281C", ["TopBarBrush"] = "#132016",
                ["SidebarBrush"] = "#0B140D→#122015", ["SidebarBorderBrush"] = "#0B140D", ["SidebarSelectedBrush"] = "#D4AF37",
                ["SidebarSelectedTextBrush"] = "#0E1A12", ["SidebarTextBrush"] = "#EFE3C2", ["SidebarMutedBrush"] = "#B8A570",
                ["SidebarHoverBrush"] = "#1FFFFFFF", ["HairlineBrush"] = "#2F4430", ["PanelBorderBrush"] = "#3A5238",
                ["SubtleFillBrush"] = "#223425", ["FillBrush"] = "#28402B", ["MutedBrush"] = "#C9BB92", ["ListHoverBrush"] = "#26392A",
                ["TextBrush"] = "#F3EAD3", ["ActionBrush"] = "#B8922A", ["PrimaryHoverBrush"] = "#CBA43A",
                ["SaveBrush"] = "#4C7A3A", ["SaveHoverBrush"] = "#5A8C46",
                ["PrimaryPressedBrush"] = "#9E7C20", ["PrimaryDisabledBrush"] = "#4A4220",
                ["PageArtFarBrush"] = "#1AEFE3C2", ["PageArtNearBrush"] = "#3D4C7A3A", ["SidebarArtBrush"] = "#1FD4AF37",
                ["PictureScrimBrush"] = "#990E1A12",
            }),
        // Ocean: sky to sand, a deep sea-blue menu, teal buttons and coral highlights; a sun, gulls and waves.
        [AppTheme.Ocean] = new("#E6F5F8→#D2EDF2→#F4E8D2", "#0E7C86", "#DDF2F3", "#C6E9EB", false,
            new()
            {
                ["SidebarBrush"] = "#08344A→#0C5A6E", ["SidebarBorderBrush"] = "#08344A", ["SidebarTextBrush"] = "#FFFFFF",
                ["SidebarMutedBrush"] = "#A9D6DE", ["SidebarHoverBrush"] = "#1FFFFFFF", ["SidebarSelectedBrush"] = "#F07A5A",
                ["SidebarSelectedTextBrush"] = "#FFFFFF", ["TopBarBrush"] = "#F7FCFC", ["CardBrush"] = "#FFFFFF", ["PopupBrush"] = "#FFFFFF",
                ["HairlineBrush"] = "#D3E8EA", ["PanelBorderBrush"] = "#C2E0E3", ["SubtleFillBrush"] = "#F2FAFA", ["FillBrush"] = "#E3F2F3",
                ["MutedBrush"] = "#4A6670", ["TextBrush"] = "#0D2A33", ["ListHoverBrush"] = "#EEF8F8",
                ["ActionBrush"] = "#0E7C86", ["PrimaryHoverBrush"] = "#13919C", ["PrimaryPressedBrush"] = "#0A626A", ["PrimaryDisabledBrush"] = "#B6DADD",
                ["SaveBrush"] = "#1F8A70", ["SaveHoverBrush"] = "#26A083",
                ["SunArtBrush"] = "#3DF07A5A", ["SkyArtBrush"] = "#4D0C5A6E", ["FarArtBrush"] = "#3D0E7C86", ["NearArtBrush"] = "#5C0C5A6E",
                ["SideArtBrush"] = "#26FFFFFF", ["PictureScrimBrush"] = "#B3E6F5F8",
            }),
        // Midnight Stars: a starry navy night, gold highlights; a crescent moon, stars, hills and a row of pines.
        [AppTheme.Midnight] = new("#070B1F→#0D1433→#18214A", "#F2C14E", "#2B2818", "#3A3420", true,
            new()
            {
                ["CardBrush"] = "#121A3A", ["PopupBrush"] = "#141C3E", ["TopBarBrush"] = "#0B1129",
                ["SidebarBrush"] = "#04071A→#0A1030", ["SidebarBorderBrush"] = "#04071A", ["SidebarSelectedBrush"] = "#F2C14E",
                ["SidebarSelectedTextBrush"] = "#0B1129", ["SidebarTextBrush"] = "#E8ECFF", ["SidebarMutedBrush"] = "#9AA6D6",
                ["SidebarHoverBrush"] = "#1FFFFFFF", ["HairlineBrush"] = "#25305E", ["PanelBorderBrush"] = "#2E3A6E",
                ["SubtleFillBrush"] = "#18214A", ["FillBrush"] = "#1E2958", ["MutedBrush"] = "#AEB8E0", ["ListHoverBrush"] = "#1A2450",
                ["TextBrush"] = "#EEF1FF", ["ActionBrush"] = "#3D5BD9", ["PrimaryHoverBrush"] = "#5470E6",
                ["PrimaryPressedBrush"] = "#2F48B5", ["PrimaryDisabledBrush"] = "#2A3466", ["SaveBrush"] = "#2FA57A", ["SaveHoverBrush"] = "#39B98A",
                ["SkyArtBrush"] = "#8CFFFFFF", ["SunArtBrush"] = "#C7F6E7B6", ["FarArtBrush"] = "#1F9AA6D6", ["NearArtBrush"] = "#D903061A",
                ["SideArtBrush"] = "#4DFFFFFF", ["PictureScrimBrush"] = "#99070B1F",
            }),
        // Eid (festival look, on by itself on Eid): a deep green night with gold.
        [FestivalLooks.Eid] = new("#021A18→#05302C→#0A4A43", "#E8C15A", "#2E3A2A", "#3A4026", true,
            new()
            {
                ["CardBrush"] = "#0B2F2B", ["PopupBrush"] = "#0D3430", ["TopBarBrush"] = "#03211E",
                ["SidebarBrush"] = "#01110F→#042824", ["SidebarBorderBrush"] = "#01110F", ["SidebarSelectedBrush"] = "#E8C15A",
                ["SidebarSelectedTextBrush"] = "#062421", ["SidebarTextBrush"] = "#E6F4F0", ["SidebarMutedBrush"] = "#A9CFC6",
                ["SidebarHoverBrush"] = "#1FFFFFFF", ["HairlineBrush"] = "#1C4A44", ["PanelBorderBrush"] = "#245750",
                ["SubtleFillBrush"] = "#0F3A35", ["FillBrush"] = "#14443E", ["MutedBrush"] = "#A9CFC6", ["ListHoverBrush"] = "#12403A",
                ["TextBrush"] = "#EEF7F4", ["ActionBrush"] = "#1E9C86", ["PrimaryHoverBrush"] = "#25B39A",
                ["PrimaryPressedBrush"] = "#187F6D", ["PrimaryDisabledBrush"] = "#1F4A44", ["SaveBrush"] = "#2FA57A", ["SaveHoverBrush"] = "#39B98A",
                ["SkyArtBrush"] = "#D9E8C15A", ["SunArtBrush"] = "#F2F2D27A", ["FarArtBrush"] = "#33E8C15A", ["NearArtBrush"] = "#E6011412",
                ["SideArtBrush"] = "#8CE8C15A", ["PictureScrimBrush"] = "#99021A18",
            }),
        // Diwali (festival look, on by itself at Diwali): a plum night with orange and gold, fireworks in pink, gold and blue.
        [FestivalLooks.Diwali] = new("#160822→#2A0C30→#44142E", "#FFB347", "#4A2A20", "#4A2A30", true,
            new()
            {
                ["CardBrush"] = "#2A1230", ["PopupBrush"] = "#301536", ["TopBarBrush"] = "#1A0824",
                ["SidebarBrush"] = "#0E0418→#2A0A26", ["SidebarBorderBrush"] = "#0E0418", ["SidebarSelectedBrush"] = "#FF9F1C",
                ["SidebarSelectedTextBrush"] = "#2A0A26", ["SidebarTextBrush"] = "#FBEFF6", ["SidebarMutedBrush"] = "#E0BFD8",
                ["SidebarHoverBrush"] = "#1FFFFFFF", ["HairlineBrush"] = "#4A2650", ["PanelBorderBrush"] = "#5A2E5E",
                ["SubtleFillBrush"] = "#341838", ["FillBrush"] = "#3E1E42", ["MutedBrush"] = "#E0BFD8", ["ListHoverBrush"] = "#3A1C3E",
                ["TextBrush"] = "#FFF4FA", ["ActionBrush"] = "#E8590C", ["PrimaryHoverBrush"] = "#FF6B1A",
                ["PrimaryPressedBrush"] = "#C44A0A", ["PrimaryDisabledBrush"] = "#4A2A3A", ["SaveBrush"] = "#2FA57A", ["SaveHoverBrush"] = "#39B98A",
                ["SkyArtBrush"] = "#E6FF5DA2⇒#E6FFC93C⇒#E66EC6FF⇒#E6FF8C42", ["SunArtBrush"] = "#F2FF5DA2→#F2FFB347",
                ["FarArtBrush"] = "#FFC0582E", ["NearArtBrush"] = "#FFFFC93C", ["SideArtBrush"] = "#D9FFB347", ["PictureScrimBrush"] = "#99160822",
            }),
        // Tally: like TallyPrime — a dark blue-green top bar, a light blue page, white boxes with thin lines, square corners and no
        // shadows; the chosen item in a yellow bar, the menu's shortcut letters in red, a key bar on the right.
        [AppTheme.Tally] = new("#E4EEF3", "#1F5F7D", "#DCEAF1", "#F9C74F", false,
            new()
            {
                ["CardBrush"] = "#FFFFFF", ["PopupBrush"] = "#FFFFFF", ["TopBarBrush"] = "#1F4D63", ["TopBarTextBrush"] = "#FFFFFF",
                ["TopBarMutedBrush"] = "#BFD6E1", ["TopBarFieldBrush"] = "#33FFFFFF",
                ["SidebarBrush"] = "#FFFFFF", ["SidebarBorderBrush"] = "#9CBCCB", ["SidebarTextBrush"] = "#13324A", ["SidebarMutedBrush"] = "#5E7480",
                ["SidebarHoverBrush"] = "#EAF2F6", ["SidebarSelectedBrush"] = "#F9C74F", ["SidebarSelectedTextBrush"] = "#1B2A33",
                ["HairlineBrush"] = "#C9DCE5", ["PanelBorderBrush"] = "#9CBCCB", ["SubtleFillBrush"] = "#F3F8FA", ["FillBrush"] = "#E8F0F4",
                ["MutedBrush"] = "#5E7480", ["TextBrush"] = "#1B2A33", ["HeadingBrush"] = "#1F5F7D", ["ListHoverBrush"] = "#EEF5F8",
                ["ListSelectedBarBrush"] = "#E0A800", ["BadgeBrush"] = "#F9C74F", ["BadgeTextBrush"] = "#1B2A33",
                ["ActionBrush"] = "#1F4D63", ["PrimaryHoverBrush"] = "#2A6684", ["PrimaryPressedBrush"] = "#173B4C", ["PrimaryDisabledBrush"] = "#B7CAD3",
                ["SaveBrush"] = "#2E7D32", ["SaveHoverBrush"] = "#388E3C", ["EditingRowBrush"] = "#FBE3A0",
                // Only the Tally theme uses these (Styles/TallyStyles.axaml)
                ["NavHotKeyBrush"] = "#C0392B", ["TallyBandBrush"] = "#CFE2EB", ["KeyBarBrush"] = "#D6E5EC", ["KeyBarLineBrush"] = "#B8CFDA",
                ["TallyHeadBrush"] = "#2A6684",
            }),
    };

    private static Dictionary<string, string> GlassBrushes(Glass g) => g.Dark
        ? new()
        {
            ["PageBrush"] = g.Wallpaper, ["CardBrush"] = "#24FFFFFF", ["PopupBrush"] = "#2A2A30", ["TopBarBrush"] = "#1AFFFFFF",
            ["SidebarBrush"] = "#14FFFFFF", ["SidebarBorderBrush"] = "#26FFFFFF", ["SidebarSelectedBrush"] = "#38FFFFFF", ["SidebarHoverBrush"] = "#1AFFFFFF",
            ["SidebarTextBrush"] = "#F2F2F7", ["SidebarMutedBrush"] = "#A1A1AA", ["SidebarSelectedTextBrush"] = "#FFFFFF",
            ["AccentBrush"] = g.Accent, ["AccentSoftBrush"] = g.AccentSoft, ["ActionBrush"] = "#0A84FF", ["PrimaryHoverBrush"] = "#3D9BFF",
            ["SaveBrush"] = "#30B350", ["SaveHoverBrush"] = "#3EC25E", ["HairlineBrush"] = "#33FFFFFF", ["PanelBorderBrush"] = "#40FFFFFF",
            ["SubtleFillBrush"] = "#14FFFFFF", ["FillBrush"] = "#1FFFFFFF", ["TopBarFieldBrush"] = "#1FFFFFFF", ["MutedBrush"] = "#A9A9B2",
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
            ["SubtleFillBrush"] = "#80FFFFFF", ["FillBrush"] = "#99FFFFFF", ["TopBarFieldBrush"] = "#99FFFFFF", ["MutedBrush"] = "#4A4A55",
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
            var extra = glass.Extra ?? [];
            foreach (var (key, colour) in extra) app.Resources[key] = BrushOf(colour);
            // The top bar follows the theme's own text and field colours unless the theme gives its own.
            void Follow(string key, string from) { if (!extra.ContainsKey(key) && extra.TryGetValue(from, out var c)) app.Resources[key] = BrushOf(c); }
            Follow("TopBarFieldBrush", "FillBrush");
            Follow("TopBarTextBrush", "TextBrush");
            Follow("TopBarMutedBrush", "MutedBrush");
            Follow("HeadingBrush", "TextBrush");
            if (!extra.ContainsKey("BadgeBrush")) app.Resources["BadgeBrush"] = BrushOf(glass.Accent);
            if (theme == AppTheme.GlassMaroon)
            {
                SetShape(app, "SidebarSelectedShadow", BoxShadows.Parse("inset 3 0 0 0 #F6E1E5"));
                SetShape(app, "TileStrip1", BoxShadows.Parse("inset 4 0 0 0 #8E1B2E"));
                SetShape(app, "TileStrip2", BoxShadows.Parse("inset 4 0 0 0 #1C1C1E"));
                SetShape(app, "TileStrip3", BoxShadows.Parse("inset 4 0 0 0 #C93400"));
                SetShape(app, "TileStrip4", BoxShadows.Parse("inset 4 0 0 0 #8944AB"));
            }
            foreach (var (key, geometry) in Controls.ThemeScenery.For(theme)) app.Resources[key] = geometry;
            if (theme == AppTheme.Tally)
            {
                // Square corners, thin lines, no shadows (as in Tally).
                SetShape(app, "CardRadius", new CornerRadius(0));
                SetShape(app, "CardBottomRadius", new CornerRadius(0));
                SetShape(app, "ButtonRadius", new CornerRadius(2));
                SetShape(app, "ControlCornerRadius", new CornerRadius(0));
                SetShape(app, "OverlayCornerRadius", new CornerRadius(0));
                SetShape(app, "SpinnerEndRadius", new CornerRadius(0));
                SetShape(app, "PopupShadow", BoxShadows.Parse("0 2 6 0 #261F4D63"));
                GlowShadow = NoShadow;
                return;
            }
            SetShape(app, "CardRadius", new CornerRadius(22));
            SetShape(app, "CardBottomRadius", new CornerRadius(0, 0, 22, 22));
            SetShape(app, "ButtonRadius", new CornerRadius(18));
            SetShape(app, "ControlCornerRadius", new CornerRadius(12));
            SetShape(app, "InputPadding", new Thickness(11, 2)); // the cursor clears the round corner
            SetShape(app, "SpinnerEndRadius", new CornerRadius(0, 12, 12, 0));
            GlowShadow = GlassShadow;
            return;
        }
        if (theme is null || !Palettes.TryGetValue(theme, out var p)) return;
        void Set(string key, string colour) => app.Resources[key] = BrushOf(colour);
        Set("AccentBrush", p.Accent);
        Set("BadgeBrush", p.Accent);
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
    }

    /// <summary>The glow around cards of the current theme (null = the standard small shadow). Off with "Reduce animations".</summary>
    public static string? GlowShadow { get; private set; }

    /// <summary>No shadow at all (the Tally theme: flat boxes with thin lines).</summary>
    public const string NoShadow = "0 0 0 0 #00000000";

    /// <summary>A flat theme (Tally): no shadows and no glow on the main buttons.</summary>
    public static bool IsFlat => GlowShadow == NoShadow;

    /// <summary>"#RRGGBB" / "#AARRGGBB", or a gradient "#A→#B(→#C)" (top to bottom) or "#A⇒#B⇒…" (left to right).</summary>
    public static IBrush BrushOf(string colour)
    {
        var across = colour.Contains('⇒');
        var parts = colour.Split(across ? '⇒' : '→');
        if (parts.Length == 1) return new SolidColorBrush(Color.Parse(colour));
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = across ? new RelativePoint(1, 0, RelativeUnit.Relative) : new RelativePoint(0.35, 1, RelativeUnit.Relative),
        };
        for (var i = 0; i < parts.Length; i++) brush.GradientStops.Add(new GradientStop(Color.Parse(parts[i]), (double)i / (parts.Length - 1)));
        return brush;
    }

    private static readonly string[] AllKeys = GlassBrushes(GlassThemes[AppTheme.GlassBlue]).Keys.Concat(GlassBrushes(GlassThemes[AppTheme.Midnight]).Keys).Concat(
    [
        "AccentBrush", "AccentSoftBrush", "SidebarBrush", "SidebarBorderBrush", "SidebarHoverBrush", "SidebarSelectedBrush", "PageBrush",
        "ListSelectedBrush", "ListSelectedHoverBrush", "ListSelectedBarBrush", "ListHoverBrush", "TextControlBorderBrushFocused",
        "LinkHoverBrush", "TileHoverBorderBrush", "TopBarFieldBrush", "TopBarTextBrush", "TopBarMutedBrush", "BadgeBrush", "BadgeTextBrush",
        "HeadingBrush",
    ]).Concat(GlassThemes.Values.SelectMany(g => g.Extra?.Keys ?? Enumerable.Empty<string>())).Concat(Controls.ThemeScenery.Keys).Distinct().ToArray();

    // Shapes (corners) live at the top of the app resources: remember the standard values to put them back.
    private static readonly string[] ShapeKeys = ["CardRadius", "CardBottomRadius", "ButtonRadius", "ControlCornerRadius", "InputPadding", "SpinnerEndRadius",
        "SidebarSelectedShadow", "TileStrip1", "TileStrip2", "TileStrip3", "TileStrip4", "OverlayCornerRadius", "PopupShadow"];
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
