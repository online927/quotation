using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace Quotation.Desktop.Controls;

/// <summary>
/// Small month calendar for a date box: ‹ Month Year ›, days Mo–Su, today ringed, the chosen day in blue.
/// Clicking a day sets <see cref="SelectedDate"/> and closes the popup it sits in.
/// </summary>
public sealed class MiniCalendar : UserControl
{
    public static readonly StyledProperty<DateTime?> SelectedDateProperty =
        AvaloniaProperty.Register<MiniCalendar, DateTime?>(nameof(SelectedDate), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public DateTime? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>The day "today" is ringed (settable for tests).</summary>
    public DateTime Today { get; set; } = DateTime.Today;

    /// <summary>The first day of the month shown.</summary>
    public DateTime Month { get; private set; }

    private readonly TextBlock _title = new() { Classes = { "caltitle" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _days = new() { Columns = 7, Rows = 6 };

    public MiniCalendar()
    {
        Focusable = true;
        var prev = new Button { Content = "‹", Classes = { "calnav" } };
        var next = new Button { Content = "›", Classes = { "calnav" } };
        prev.Click += (_, _) => ShowMonth(Month.AddMonths(-1));
        next.Click += (_, _) => ShowMonth(Month.AddMonths(1));
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(prev);
        Grid.SetColumn(_title, 1);
        head.Children.Add(_title);
        Grid.SetColumn(next, 2);
        head.Children.Add(next);

        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var n in new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" })
            names.Children.Add(new TextBlock { Text = n, Classes = { "calweek" }, HorizontalAlignment = HorizontalAlignment.Center });

        Content = new StackPanel { Children = { head, names, _days } };
        ShowMonth(new DateTime(Today.Year, Today.Month, 1));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedDateProperty)
            ShowMonth(SelectedDate is { } d ? new DateTime(d.Year, d.Month, 1) : new DateTime(Today.Year, Today.Month, 1));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            ClosePopup();
            e.Handled = true;
        }
    }

    public void ShowMonth(DateTime firstOfMonth)
    {
        Month = new DateTime(firstOfMonth.Year, firstOfMonth.Month, 1);
        _title.Text = Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        _days.Children.Clear();
        var start = Month.AddDays(-(((int)Month.DayOfWeek + 6) % 7)); // back to Monday
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var b = new Button { Content = day.Day.ToString(CultureInfo.InvariantCulture), Classes = { "calday" }, Tag = day };
            if (day.Month != Month.Month) b.Classes.Add("other");
            if (day == Today.Date) b.Classes.Add("today");
            if (SelectedDate?.Date == day) b.Classes.Add("selected");
            b.Click += (_, _) => Pick(day);
            _days.Children.Add(b);
        }
    }

    /// <summary>Chooses a day (as a click does).</summary>
    public void Pick(DateTime day)
    {
        SelectedDate = day.Date;
        ClosePopup();
    }

    private void ClosePopup()
    {
        if (this.FindLogicalAncestorOfType<Popup>() is { } popup) popup.IsOpen = false;
    }
}
