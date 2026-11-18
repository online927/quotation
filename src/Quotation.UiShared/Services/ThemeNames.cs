using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>The themes in the order of the list in My Settings, with the names people see.</summary>
public static class ThemeNames
{
    public static readonly IReadOnlyList<(string Theme, string Name)> All =
    [
        (AppTheme.GlassBlue, "Glass (Blue)"), (AppTheme.Graphite, "Graphite"), (AppTheme.GlassGreen, "Grass Green"),
        (AppTheme.GlassMaroon, "Glass (Maroon)"), (AppTheme.Parchment, "Parchment"), (AppTheme.ForestGold, "Forest & Gold"),
        (AppTheme.HeroLight, "Hero Light"), (AppTheme.Ocean, "Ocean"),
        (AppTheme.Midnight, "Midnight Stars"), (AppTheme.Tally, "Tally"),
    ];

    public static string Of(string? theme) => All.FirstOrDefault(t => t.Theme == theme).Name ?? theme ?? "";
}
