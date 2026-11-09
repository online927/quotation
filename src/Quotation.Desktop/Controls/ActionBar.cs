using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Quotation.Desktop.Controls;

/// <summary>
/// The buttons under a quotation, always on one line. Children marked <c>ActionBar.Right="True"</c> sit at the right end.
/// When the line is too narrow, buttons with a <c>ActionBar.CollapseOrder</c> (1 first, then 2 …) show only their icon
/// (the class "iconly" hides their text; the name stays in the tooltip). Buttons without an order always keep their text.
/// </summary>
public sealed class ActionBar : Panel
{
    public static readonly AttachedProperty<bool> RightProperty =
        AvaloniaProperty.RegisterAttached<ActionBar, Control, bool>("Right");
    public static readonly AttachedProperty<int> CollapseOrderProperty =
        AvaloniaProperty.RegisterAttached<ActionBar, Control, int>("CollapseOrder");
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ActionBar, double>(nameof(Spacing), 8);
    /// <summary>Space between the left group and the right group.</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<ActionBar, double>(nameof(Gap), 16);

    public static bool GetRight(Control c) => c.GetValue(RightProperty);
    public static void SetRight(Control c, bool value) => c.SetValue(RightProperty, value);
    public static int GetCollapseOrder(Control c) => c.GetValue(CollapseOrderProperty);
    public static void SetCollapseOrder(Control c, int value) => c.SetValue(CollapseOrderProperty, value);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    static ActionBar() => AffectsMeasure<ActionBar>(SpacingProperty, GapProperty);

    // Width of each collapsible child with its text, remembered while it shows its text.
    private readonly Dictionary<Control, double> _fullWidth = [];
    private readonly Dictionary<Control, double> _iconWidth = [];

    public static bool IsIconOnly(Control c) => c.Classes.Contains("iconly");

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children) child.Measure(infinite);

        var collapsible = Children.Where(c => c.IsVisible && GetCollapseOrder(c) > 0).OrderBy(GetCollapseOrder).ToList();
        foreach (var c in collapsible)
        {
            if (IsIconOnly(c)) _iconWidth[c] = c.DesiredSize.Width;
            else _fullWidth[c] = c.DesiredSize.Width;
        }

        double Width(Control c) => GetCollapseOrder(c) > 0 && _fullWidth.TryGetValue(c, out var full) ? full : c.DesiredSize.Width;
        var total = Total(Width);
        var collapse = new HashSet<Control>();
        if (!double.IsInfinity(availableSize.Width))
        {
            foreach (var c in collapsible)
            {
                if (total <= availableSize.Width) break;
                collapse.Add(c);
                total -= Width(c) - _iconWidth.GetValueOrDefault(c, 40);
            }
        }

        // Change only what differs, so a second pass makes the same choice and the layout settles.
        var changed = false;
        foreach (var c in collapsible)
        {
            var want = collapse.Contains(c);
            if (want == IsIconOnly(c)) continue;
            if (want) c.Classes.Add("iconly"); else c.Classes.Remove("iconly");
            c.Measure(infinite);
            changed = true;
        }
        if (changed)
            foreach (var c in collapsible.Where(IsIconOnly)) _iconWidth[c] = c.DesiredSize.Width;

        var height = Children.Where(c => c.IsVisible).Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();
        var used = Total(c => c.DesiredSize.Width);
        return new Size(double.IsInfinity(availableSize.Width) ? used : Math.Min(used, availableSize.Width), height);
    }

    private double Total(Func<Control, double> width)
    {
        var left = Children.Where(c => c.IsVisible && !GetRight(c)).ToList();
        var right = Children.Where(c => c.IsVisible && GetRight(c)).ToList();
        double Sum(List<Control> list) => list.Sum(width) + Math.Max(0, list.Count - 1) * Spacing;
        return Sum(left) + Sum(right) + (left.Count > 0 && right.Count > 0 ? Gap : 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double Place(Control c, double x)
        {
            var w = c.DesiredSize.Width;
            var h = c.VerticalAlignment == VerticalAlignment.Stretch ? finalSize.Height : c.DesiredSize.Height;
            var y = c.VerticalAlignment switch
            {
                VerticalAlignment.Top => 0,
                VerticalAlignment.Bottom => finalSize.Height - h,
                _ => (finalSize.Height - h) / 2,
            };
            c.Arrange(new Rect(x, y, w, h));
            return w;
        }
        var x = 0.0;
        foreach (var c in Children.Where(c => c.IsVisible && !GetRight(c))) x += Place(c, x) + Spacing;
        var right = Children.Where(c => c.IsVisible && GetRight(c)).ToList();
        var rx = finalSize.Width - right.Sum(c => c.DesiredSize.Width) - Math.Max(0, right.Count - 1) * Spacing;
        foreach (var c in right) rx += Place(c, rx) + Spacing;
        return finalSize;
    }
}
