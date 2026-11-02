using Avalonia;
using Avalonia.Styling;
using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>Switches the screens between light and dark (each user's choice; PDFs are always white).</summary>
public static class ThemeService
{
    public static void Apply(string? theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.System => ThemeVariant.Default,
            _ => ThemeVariant.Light,
        };
    }

    /// <summary>The screens are dark now (chosen, or Windows is dark when following Windows).</summary>
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
}
