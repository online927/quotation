using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class FormaterView : UserControl
{
    private static readonly IBrush ProblemBand = new SolidColorBrush(Color.Parse("#24E5484D"));
    private static readonly IBrush ProblemBar = new SolidColorBrush(Color.Parse("#E5484D"));
    private FormaterViewModel? _vm;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroller;
    private bool _redrawPending;

    public FormaterView()
    {
        InitializeComponent();
        FormaterTextBox.PastingFromClipboard += OnPasting;
        FormaterTextBox.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(HookTextBox, DispatcherPriority.Loaded);
        FormaterTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty || e.Property == BoundsProperty) RedrawSoon();
        };
        LineGutter.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) RedrawSoon(); };
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FormaterTextBox.Focus();
        HookTextBox();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as FormaterViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        RedrawSoon();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FormaterViewModel.ProblemLines) or nameof(FormaterViewModel.Text)) RedrawSoon();
    }

    /// <summary>Alt+P: the cursor in the P&amp;F box with its amount selected.</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.P || e.KeyModifiers != KeyModifiers.Alt) return;
        FocusPacking();
        e.Handled = true;
    }

    public void FocusPacking()
    {
        FormaterPackingBox.Focus(NavigationMethod.Tab);
        if (FormaterPackingBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
        {
            box.Focus(NavigationMethod.Tab);
            box.SelectAll();
        }
    }

    // ---------------------------------------------------------------- paste

    private async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        e.Handled = true; // put in below, cleaned
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception) { return; }
        if (string.IsNullOrEmpty(text)) return;
        PasteText(text);
    }

    /// <summary>What Ctrl+V in the text box does with <paramref name="text"/> (also used by tests).</summary>
    public void PasteText(string text)
    {
        if (_vm is null) return;
        var box = FormaterTextBox;
        var start = Math.Min(box.SelectionStart, box.SelectionEnd);
        var length = Math.Abs(box.SelectionEnd - box.SelectionStart);
        if (length == 0) start = box.CaretIndex;
        var caret = _vm.Paste(text, start, length);
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = Math.Min(caret, box.Text?.Length ?? 0);
    }

    // ---------------------------------------------------------------- go to a line

    private void OnGoToLineClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FormaterProblem { Line: > 0 } problem }) GoToLine(problem.Line);
    }

    /// <summary>The cursor on line <paramref name="line"/> (1 = the first), the whole line selected.</summary>
    public void GoToLine(int line)
    {
        var text = FormaterTextBox.Text ?? "";
        var starts = LineStarts(text);
        if (line < 1 || line > starts.Count) return;
        var start = starts[line - 1];
        var end = line < starts.Count ? starts[line] - 1 : text.Length;
        while (end > start && end <= text.Length && (text[end - 1] == '\r' || text[end - 1] == '\n')) end--;
        FormaterTextBox.Focus();
        FormaterTextBox.ScrollToLine(line - 1);
        FormaterTextBox.CaretIndex = end;
        FormaterTextBox.SelectionStart = start;
        FormaterTextBox.SelectionEnd = end; // (the caret is at the end of the selection)
        RedrawSoon();
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    // ---------------------------------------------------------------- line numbers and red lines

    private void HookTextBox()
    {
        var presenter = FormaterTextBox.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (presenter is null || presenter == _presenter) return;
        _presenter = presenter;
        // (Not LayoutUpdated: it comes after every layout pass anywhere and would redraw all the time.)
        _presenter.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) RedrawSoon(); };
        _scroller = FormaterTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroller is not null) _scroller.ScrollChanged += (_, _) => RedrawSoon();
        RedrawSoon();
    }

    private void RedrawSoon()
    {
        if (_redrawPending) return;
        _redrawPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _redrawPending = false;
            Redraw();
        }, DispatcherPriority.Background); // after the text box has laid out the new text
    }

    /// <summary>Numbers the lines that can be seen, at the height of each line of the text box; lines with a problem in red.</summary>
    private void Redraw()
    {
        LineGutter.Children.Clear();
        LineMarks.Children.Clear();
        if (_presenter is null) HookTextBox();
        if (_presenter is not { } presenter) return;
        var text = FormaterTextBox.Text ?? "";
        var starts = LineStarts(text);
        var bad = new HashSet<int>(_vm?.ProblemLines ?? []);
        var layout = presenter.TextLayout;
        var gutterHeight = LineGutter.Bounds.Height;
        var width = LineMarks.Bounds.Width;
        var font = FormaterTextBox.FontFamily;
        var size = FormaterTextBox.FontSize;
        for (var n = 1; n <= starts.Count; n++)
        {
            var index = Math.Min(starts[n - 1], text.Length);
            Rect r;
            try { r = layout.HitTestTextPosition(index); }
            catch (Exception) { continue; }
            var top = presenter.TranslatePoint(new Point(0, r.Y), LineGutter);
            if (top is not { } p) continue;
            var h = r.Height > 0 ? r.Height : size * 1.3;
            if (p.Y + h < 0) continue;
            if (p.Y > gutterHeight) break;
            var isBad = bad.Contains(n);
            var number = new TextBlock
            {
                Text = n.ToString(), FontFamily = font, FontSize = size - 1, Width = 30, Height = h,
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isBad ? FontWeight.Bold : FontWeight.Normal,
            };
            if (isBad) number.Foreground = ProblemBar;
            else number.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush")); // follows the theme
            Canvas.SetLeft(number, 0);
            Canvas.SetTop(number, p.Y + 1);
            LineGutter.Children.Add(number);
            if (!isBad) continue;
            var bar = new Rectangle { Width = 3, Height = h, Fill = ProblemBar };
            Canvas.SetLeft(bar, 0);
            Canvas.SetTop(bar, p.Y);
            LineGutter.Children.Add(bar);
            if (presenter.TranslatePoint(new Point(0, r.Y), LineMarks) is { } q)
            {
                var band = new Rectangle { Width = Math.Max(0, width - 2), Height = h, Fill = ProblemBand };
                Canvas.SetLeft(band, 1);
                Canvas.SetTop(band, q.Y);
                LineMarks.Children.Add(band);
            }
        }
    }
}
