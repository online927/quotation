using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Quotation.Desktop.Views;

public partial class MainWindow : Window
{
    /// <summary>Ask before closing (tests switch it off).</summary>
    public static bool ConfirmOnClose { get; set; } = true;

    private bool _closeConfirmed;

    /// <summary>
    /// Closing the window (✕, Alt+F4): asks first - Save / Don't Save / Cancel with unsaved work, otherwise
    /// "Close TS Quotation?". When Windows shuts down it does not ask; unsaved work is kept on the PC for next time.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !ConfirmOnClose || DataContext is not ViewModels.MainWindowViewModel { Content: ViewModels.ShellViewModel shell }) return;
        if (e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown || e.IsProgrammatic)
        {
            shell.KeepWorkForNextTime();
            return;
        }
        e.Cancel = true;
        _ = AskThenCloseAsync(shell);
    }

    private async Task AskThenCloseAsync(ViewModels.ShellViewModel shell)
    {
        if (!await shell.ConfirmCloseAsync()) return;
        _closeConfirmed = true;
        Close();
    }

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyTitleBarColour();
        ApplyZoom(Services.ThemeService.Zoom);
        Services.ThemeService.ZoomChanged += ApplyZoom;
        Closed += (_, _) => Services.ThemeService.ZoomChanged -= ApplyZoom;
        ActualThemeVariantChanged += (_, _) => ApplyTitleBarColour();
        Services.ThemeService.ColoursChanged += ApplyTitleBarColour;
        Closed += (_, _) => Services.ThemeService.ColoursChanged -= ApplyTitleBarColour;
    }

    /// <summary>
    /// The title bar takes the colours of the theme's top bar (maroon blush in Glass (Maroon), parchment in Parchment,
    /// dark green in Forest &amp; Gold …) instead of the Windows accent colour, so it looks like one piece with the app.
    /// Windows 11 only (its title-bar colour setting); elsewhere the title bar stays as Windows draws it.
    /// </summary>
    private void ApplyTitleBarColour()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        var (caption, text, border) = Services.ThemeService.TitleBarColours();
        static int Rgb(Avalonia.Media.Color c) => (c.R << 16) | (c.G << 8) | c.B;
        try
        {
            SetColour(hwnd, DwmCaptionColour, Rgb(caption));
            SetColour(hwnd, DwmTextColour, Rgb(text));
            SetColour(hwnd, DwmBorderColour, Rgb(border));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not available: keep the Windows title bar.
        }
    }

    private void ApplyZoom(double zoom) =>
        Zoom.LayoutTransform = Math.Abs(zoom - 1) < 0.001 ? null : new Avalonia.Media.ScaleTransform(zoom, zoom);

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
