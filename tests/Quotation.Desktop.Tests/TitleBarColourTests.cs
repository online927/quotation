using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.Tests;

/// <summary>The Windows title bar takes each theme's top-bar colours.</summary>
public class TitleBarColourTests
{
    [AvaloniaFact]
    public void Title_bar_follows_the_theme()
    {
        try
        {
            ThemeService.Apply(AppTheme.GlassMaroon);
            Assert.Equal(Color.Parse("#F9ECEF"), ThemeService.TitleBarColours().Caption);
            ThemeService.Apply(AppTheme.Parchment);
            Assert.Equal(Color.Parse("#F6ECD4"), ThemeService.TitleBarColours().Caption);
            Assert.Equal(Color.Parse("#2A2114"), ThemeService.TitleBarColours().Text);
            ThemeService.Apply(AppTheme.ForestGold);
            Assert.Equal(Color.Parse("#132016"), ThemeService.TitleBarColours().Caption);
            Assert.Equal(Color.Parse("#F3EAD3"), ThemeService.TitleBarColours().Text);

            // Every theme gives a solid bar with readable text.
            foreach (var theme in AppTheme.All.Where(t => t != AppTheme.System))
            {
                ThemeService.Apply(theme);
                var (caption, text, _) = ThemeService.TitleBarColours();
                Assert.Equal(255, caption.A);
                Assert.True(Contrast(caption, text) >= 4.5, $"{theme}: title text contrast {Contrast(caption, text):0.0}");
            }
        }
        finally
        {
            ThemeService.Apply(AppTheme.Light);
        }
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var (x, y) = (L(a), L(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
