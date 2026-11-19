using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Quotation.Desktop.Services;

/// <summary>
/// Sharp text (My Settings, this PC; on at first): letters drawn with ClearType-style edges on Windows (sharper than
/// the plain grey edges), and the smallest text half a point bigger. Off: exactly the old look.
/// </summary>
public static class SharpTextLook
{
    /// <summary>The small text sizes (FontSmall / FontTiny resources) with Sharp text off and on.</summary>
    public const double SmallOff = 11, SmallOn = 11.5, TinyOff = 10.5, TinyOn = 11;

    private static bool _hooked;

    public static bool IsOn { get; private set; }

    /// <summary>How letters are drawn now: ClearType-style on Windows with Sharp text on, else the standard.</summary>
    public static TextRenderingMode Mode => IsOn && OperatingSystem.IsWindows() ? TextRenderingMode.SubpixelAntialias : TextRenderingMode.Unspecified;

    public static void Apply(bool on)
    {
        IsOn = on;
        if (Application.Current is not { } app) return;
        app.Resources["FontSmall"] = on ? SmallOn : SmallOff;
        app.Resources["FontTiny"] = on ? TinyOn : TinyOff;
        if (!_hooked)
        {
            _hooked = true;
            // Every window, and every pop-up (lists, menus, hints) when it opens, draws its letters this way
            Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => RenderOptions.SetTextRenderingMode(w, Mode));
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, e) =>
            {
                if (e.NewValue is true && p.Host is Visual host) RenderOptions.SetTextRenderingMode(host, Mode);
            });
        }
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var window in desktop.Windows)
            {
                RenderOptions.SetTextRenderingMode(window, Mode);
                window.InvalidateVisual();
            }
    }
}
