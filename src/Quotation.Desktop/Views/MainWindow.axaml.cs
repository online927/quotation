using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Quotation.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyTitleBarColour();
        ActualThemeVariantChanged += (_, _) => ApplyTitleBarColour();
    }

    /// <summary>
    /// White title bar with dark text (dark bar with light text in dark mode) instead of the Windows accent colour.
    /// Windows 11 only (its title-bar colour setting); elsewhere the title bar stays as Windows draws it.
    /// </summary>
    private void ApplyTitleBarColour()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        try
        {
            SetColour(hwnd, DwmCaptionColour, dark ? 0x1C1C1E : 0xFFFFFF);
            SetColour(hwnd, DwmTextColour, dark ? 0xF2F2F7 : 0x1D1D1F);
            SetColour(hwnd, DwmBorderColour, dark ? 0x3A3A3C : 0xE5E5EA);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not available: keep the Windows title bar.
        }
    }

    private const int DwmBorderColour = 34, DwmCaptionColour = 35, DwmTextColour = 36;

    /// <summary>Colour as #RRGGBB; Windows wants 0x00BBGGRR.</summary>
    private static void SetColour(IntPtr hwnd, int attribute, int rgb)
    {
        var bgr = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
        DwmSetWindowAttribute(hwnd, attribute, ref bgr, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
