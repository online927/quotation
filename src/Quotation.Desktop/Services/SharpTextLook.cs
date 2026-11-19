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

    /// <summary>The windows open now (all of them, not only those the desktop lifetime lists).</summary>
    private static readonly List<WeakReference<Window>> Open = [];

    public static bool IsOn { get; private set; }

    /// <summary>Whether this computer draws ClearType-style letters (Windows); tests set it to try the Windows look.</summary>
    internal static bool ClearTypeHere { get; set; } = OperatingSystem.IsWindows();

    /// <summary>How letters are drawn now: ClearType-style on Windows with Sharp text on, else the standard.</summary>
    public static TextRenderingMode Mode => IsOn && ClearTypeHere ? TextRenderingMode.SubpixelAntialias : TextRenderingMode.Unspecified;

    /// <summary>Watches windows and pop-ups as they open; called once at start, before the first window opens.</summary>
    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        // Every window, and every pop-up (lists, menus, hints) when it opens, draws its letters this way
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            lock (Open)
            {
                Open.RemoveAll(r => !r.TryGetTarget(out var x) || ReferenceEquals(x, w));
                Open.Add(new WeakReference<Window>(w));
            }
            RenderOptions.SetTextRenderingMode(w, Mode);
        });
        Window.WindowClosedEvent.AddClassHandler<Window>((w, _) =>
        {
            lock (Open) Open.RemoveAll(r => !r.TryGetTarget(out var x) || ReferenceEquals(x, w));
        });
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, e) =>
        {
            if (e.NewValue is true && p.Host is Visual host) RenderOptions.SetTextRenderingMode(host, Mode);
        });
    }

    public static void Apply(bool on)
    {
        IsOn = on;
        if (Application.Current is not { } app) return;
        app.Resources["FontSmall"] = on ? SmallOn : SmallOff;
        app.Resources["FontTiny"] = on ? TinyOn : TinyOff;
        Hook();
        var windows = new HashSet<Window>();
        lock (Open)
            foreach (var r in Open)
                if (r.TryGetTarget(out var w)) windows.Add(w);
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            windows.UnionWith(desktop.Windows);
        foreach (var window in windows)
        {
            RenderOptions.SetTextRenderingMode(window, Mode);
            window.InvalidateVisual();
        }
    }
}
