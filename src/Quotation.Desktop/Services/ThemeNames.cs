using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>The themes in the order of the list in My Settings, with the names people see.</summary>
public static class ThemeNames
{
    public static readonly IReadOnlyList<(string Theme, string Name)> All =
    [
        (AppTheme.Light, "Default Light"), (AppTheme.Dark, "Default Dark"), (AppTheme.System, "Same as Windows"),
        (AppTheme.Sunset, "Sunset"), (AppTheme.Graphite, "Graphite"), (AppTheme.Frost, "Frost"),
        (AppTheme.GlassBlue, "Glass (Blue)"), (AppTheme.GlassBlack, "Glass (Black)"), (AppTheme.GlassGreen, "Glass (Green)"),
        (AppTheme.GlassMaroon, "Glass (Maroon)"), (AppTheme.Parchment, "Parchment"), (AppTheme.ForestGold, "Forest & Gold"),
        (AppTheme.HeroLight, "Hero Light"), (AppTheme.NeonNight, "Neon Night"), (AppTheme.Ocean, "Ocean"),
        (AppTheme.Midnight, "Midnight Stars"),
    ];

    public static string Of(string? theme) => All.FirstOrDefault(t => t.Theme == theme).Name ?? theme ?? "";
}
