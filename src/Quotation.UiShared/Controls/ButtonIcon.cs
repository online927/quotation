using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Quotation.Desktop.Controls;

/// <summary>
/// A small line symbol drawn in the colour of the text around it (so it follows every button style and theme).
/// The shapes are 24 × 24 line drawings.
/// </summary>
public sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<StrokeIcon, string?>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<StrokeIcon>();

    static StrokeIcon() => AffectsRender<StrokeIcon>(KindProperty, ForegroundProperty);

    public StrokeIcon()
    {
        Width = 15;
        Height = 15;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static readonly Dictionary<string, Geometry> Cache = [];

    public static Geometry? GeometryOf(string? kind)
    {
        if (kind is null || !ButtonIcon.Paths.TryGetValue(kind, out var data)) return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(kind, out var g)) Cache[kind] = g = Geometry.Parse(data);
            return g;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (GeometryOf(Kind) is not { } geometry || Foreground is not { } brush) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var pen = new Pen(brush, 2.0, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>
/// A symbol in front of a button's text: <c>ctl:ButtonIcon.Kind="refresh"</c>. The button keeps its text as its content;
/// only the way it is shown changes (symbol + text).
/// </summary>
public static class ButtonIcon
{
    public static readonly AttachedProperty<string?> KindProperty =
        AvaloniaProperty.RegisterAttached<ContentControl, string?>("Kind", typeof(ButtonIcon));

    static ButtonIcon() => KindProperty.Changed.AddClassHandler<ContentControl>((control, e) =>
    {
        var kind = e.NewValue as string;
        control.ContentTemplate = kind is null ? null : new FuncDataTemplate<object?>((content, _) =>
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            panel.Children.Add(new StrokeIcon { Kind = kind });
            panel.Children.Add(new TextBlock { Text = content?.ToString(), VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }, supportsRecycling: false);
    });

    public static string? GetKind(ContentControl control) => control.GetValue(KindProperty);
    public static void SetKind(ContentControl control, string? value) => control.SetValue(KindProperty, value);

    /// <summary>The symbols (24 × 24 line drawings).</summary>
    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["refresh"] = "M20 11a8 8 0 1 0-2.3 5.7 M20 4v7h-7",
        ["save"] = "M5 3h11l3 3v15H5z M8 3v5h7V3 M8 21v-7h8v7",
        ["check"] = "M5 12l5 5 9-10",
        ["x"] = "M6 6l12 12M18 6L6 18",
        ["plus"] = "M12 5v14M5 12h14",
        ["trash"] = "M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13 M10 11v6 M14 11v6",
        ["eye"] = "M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z",
        ["download"] = "M12 4v11 M7 10l5 5 5-5 M5 20h14",
        ["export"] = "M12 15V3 M7 8l5-5 5 5 M5 13v7h14v-7",
        ["folder"] = "M3 6h6l2 2h10v11H3z",
        ["folderopen"] = "M3 19V6h6l2 2h8v3 M3 19l3-8h16l-3 8z",
        ["image"] = "M3 5h18v14H3z M8.5 8.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z M21 16l-5-5-9 8",
        ["qr"] = "M4 4h6v6H4z M14 4h6v6h-6z M4 14h6v6H4z M14 14h2v2h-2z M18 18h2v2h-2z M18 14h2 M14 19h2",
        ["link"] = "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1 M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1",
        ["unlink"] = "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1 M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1 M3 3l18 18",
        ["search"] = "M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14z M20 20l-4-4",
        ["mailcheck"] = "M3 5h18v10 M3 5v14h10 M3.5 6.5L12 13l8.5-6.5 M15 19l2 2 4-4",
        ["sparkle"] = "M12 3l2 5.5L19.5 10 14 12l-2 6-2-6-5.5-2L10 8.5z",
        ["plug"] = "M9 3v5 M15 3v5 M6 8h12v3a6 6 0 0 1-12 0z M12 17v4",
        ["sync"] = "M4 12a8 8 0 0 1 14-5.3L20 9 M20 4v5h-5 M20 12a8 8 0 0 1-14 5.3L4 15 M4 20v-5h5",
        ["db"] = "M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3z M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6 M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3",
        ["tag"] = "M3 12V3h9l9 9-9 9z M7.5 7.5h.01",
        ["stop"] = "M6 6h12v12H6z",
        ["restore"] = "M4 12a8 8 0 1 0 2.3-5.7 M4 4v5h5",
        ["fileplus"] = "M6 3h8l5 5v13H6z M14 3v5h5 M12.5 11v7 M9 14.5h7",
        ["open"] = "M14 4h6v6 M20 4l-9 9 M18 14v6H4V6h6",
        ["pencil"] = "M4 20h4L19 9l-4-4L4 16z M14 6l4 4",
        ["copy"] = "M8 8h12v12H8z M4 16V4h12",
        ["filerefresh"] = "M6 3h8l5 5v13H6z M14 3v5h5 M9.5 15.5a3 3 0 1 0 .9-2.1 M9.5 11.5v2h2",
        ["hash"] = "M5 9h14 M5 15h14 M10 4L8 20 M16 4l-2 16",
        ["key"] = "M7.5 11a3.5 3.5 0 1 0 0 7a3.5 3.5 0 1 0 0-7z M10 12.5l9-9 M16 6.5l3 3 M13.5 9l2 2",
        ["userplus"] = "M9 4a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M2 21c1-4 4-6 7-6s6 2 7 6 M19 8v6 M16 11h6",
        ["undo"] = "M9 14L4 9l5-5 M4 9h10a6 6 0 0 1 0 12h-3",
        ["eraser"] = "M14 4l6 6-9 9H6l-3-3z M9 9l6 6 M11 20h10",
        ["history"] = "M4 12a8 8 0 1 0 2.3-5.7 M4 4v5h5 M12 8v4l3 2",
        ["logout"] = "M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3 M10 8l-4 4 4 4 M6 12h10",
        ["play"] = "M7 4l13 8-13 8z",
        ["xcircle"] = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M9 9l6 6 M15 9l-6 6",
        ["type"] = "M5 6V4h14v2 M12 4v16 M9 20h6",
        ["arrow"] = "M5 12h14 M13 6l6 6-6 6",
        ["listplus"] = "M4 6h10 M4 12h10 M4 18h6 M17 14v6 M14 17h6",
        ["shieldcheck"] = "M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6z M8.5 12l2.5 2.5 4.5-5",
        ["upload"] = "M12 20V9 M7 14l5-5 5 5 M5 4h14",
        ["keyboard"] = "M2.5 6h19v12h-19z M6 10h.01 M9.5 10h.01 M13 10h.01 M16.5 10h.01 M6 14h.01 M18 14h.01 M9 14h6",
        ["percent"] = "M19 5L5 19 M6.5 4a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5z M17.5 15a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5z",
    };
}
