using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Quotation.Desktop.Controls;

public sealed class ItemPickedEventArgs(object item) : EventArgs
{
    public object Item { get; } = item;
}

/// <summary>
/// Tally-style "List of Items" search box. While typing, the matches are listed with the first one
/// highlighted. Up/Down/PageUp/PageDown move the highlight without changing the typed text; Enter or a
/// mouse click accepts the highlighted item and raises <see cref="ItemPicked"/>; Esc closes the list.
/// Setting <see cref="Text"/> from code (e.g. the view model clearing the entry) never starts a search.
/// </summary>
public sealed class SearchPicker : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchPicker, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<SearchPicker, string?>(nameof(Watermark));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SearchPicker, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<Func<string?, CancellationToken, Task<IEnumerable<object>>>?> SearchProperty =
        AvaloniaProperty.Register<SearchPicker, Func<string?, CancellationToken, Task<IEnumerable<object>>>?>(nameof(Search));

    public static readonly StyledProperty<double> MaxDropDownHeightProperty =
        AvaloniaProperty.Register<SearchPicker, double>(nameof(MaxDropDownHeight), 400);

    public static readonly StyledProperty<double> MinDropDownWidthProperty =
        AvaloniaProperty.Register<SearchPicker, double>(nameof(MinDropDownWidth));

    public static readonly StyledProperty<string> NoMatchTextProperty =
        AvaloniaProperty.Register<SearchPicker, string>(nameof(NoMatchText), "No match");

    public static readonly StyledProperty<TimeSpan> SearchDelayProperty =
        AvaloniaProperty.Register<SearchPicker, TimeSpan>(nameof(SearchDelay), TimeSpan.FromMilliseconds(120));

    private readonly TextBox _textBox;
    private readonly Popup _popup;
    private readonly Border _dropDown;
    private readonly ListBox _list;
    private readonly TextBlock _noMatch;
    private CancellationTokenSource? _searchCts;
    private Task _pendingSearch = Task.CompletedTask;
    private int _searchVersion;
    private bool _searching;
    private bool _settingText;

    public SearchPicker()
    {
        Focusable = true;
        IsTabStop = false;

        _textBox = new TextBox();
        _textBox.PropertyChanged += OnTextBoxPropertyChanged;
        _textBox.AddHandler(KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Tunnel);
        _textBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(CloseIfFocusLeft, DispatcherPriority.Input);

        _list = new ListBox
        {
            Focusable = false,
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        // Items never take focus, so the typing caret stays in the text box while the list is used.
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(FocusableProperty, false), new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 3)) },
        });
        _list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        _noMatch = new TextBlock { Margin = new Thickness(10, 8), Foreground = Brushes.DimGray, IsVisible = false };

        _dropDown = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#9AABBF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            BoxShadow = BoxShadows.Parse("0 4 14 0 #33000000"),
            Child = new Panel { Children = { _list, _noMatch } },
        };

        _popup = new Popup
        {
            PlacementTarget = _textBox,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            OverlayDismissEventPassThrough = true,
            Child = _dropDown,
        };

        Content = new Panel { Children = { _textBox, _popup } };
        ApplyProperties();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Returns the matches for the typed text (called after <see cref="SearchDelay"/>).</summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>>? Search
    {
        get => GetValue(SearchProperty);
        set => SetValue(SearchProperty, value);
    }

    public double MaxDropDownHeight
    {
        get => GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public double MinDropDownWidth
    {
        get => GetValue(MinDropDownWidthProperty);
        set => SetValue(MinDropDownWidthProperty, value);
    }

    public string NoMatchText
    {
        get => GetValue(NoMatchTextProperty);
        set => SetValue(NoMatchTextProperty, value);
    }

    public TimeSpan SearchDelay
    {
        get => GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    /// <summary>Raised when the user accepts an item (Enter or click).</summary>
    public event EventHandler<ItemPickedEventArgs>? ItemPicked;

    public TextBox InputBox => _textBox;
    public bool IsDropDownOpen => _popup.IsOpen;
    public IReadOnlyList<object> Results => _list.ItemsSource as IReadOnlyList<object> ?? [];
    public int HighlightedIndex => _list.SelectedIndex;
    public ListBox ResultList => _list;

    /// <summary>True when Enter belongs to this box: the list is open or a search for the typed text is still running.</summary>
    public bool HandlesEnter => _popup.IsOpen || _searching;

    /// <summary>Searches for the current text immediately and shows the list.</summary>
    public void SearchNow() => StartSearch(immediate: true);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            if (_settingText) return;
            // Set from code or a binding: show it, but never search or open the list for it.
            CancelSearch();
            ClosePopup();
            SetBoxText(change.GetNewValue<string?>());
        }
        else if (change.Property == WatermarkProperty || change.Property == ItemTemplateProperty ||
                 change.Property == MaxDropDownHeightProperty || change.Property == NoMatchTextProperty)
        {
            ApplyProperties();
        }
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.Source, this)) _textBox.Focus(e.NavigationMethod);
    }

    private void ApplyProperties()
    {
        _textBox.Watermark = Watermark;
        _list.ItemTemplate = ItemTemplate;
        _list.MaxHeight = MaxDropDownHeight;
        _noMatch.Text = NoMatchText;
    }

    private void SetBoxText(string? text)
    {
        _settingText = true;
        try
        {
            _textBox.Text = text ?? "";
            _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
        }
        finally
        {
            _settingText = false;
        }
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty || _settingText) return;
        // The user typed, pasted or deleted text.
        _settingText = true;
        try
        {
            SetCurrentValue(TextProperty, _textBox.Text);
        }
        finally
        {
            _settingText = false;
        }
        StartSearch(immediate: false);
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;
        switch (e.Key)
        {
            case Key.Down when _popup.IsOpen:
                MoveHighlight(1);
                e.Handled = true;
                break;
            case Key.Down when !string.IsNullOrWhiteSpace(_textBox.Text):
                SearchNow();
                e.Handled = true;
                break;
            case Key.Up when _popup.IsOpen:
                MoveHighlight(-1);
                e.Handled = true;
                break;
            case Key.PageDown when _popup.IsOpen:
                MoveHighlight(8);
                e.Handled = true;
                break;
            case Key.PageUp when _popup.IsOpen:
                MoveHighlight(-8);
                e.Handled = true;
                break;
            case Key.Enter when _searching:
                // Typed quickly and pressed Enter: accept the top match once the results for this text arrive.
                e.Handled = true;
                _ = PickWhenSearchCompletesAsync(_searchVersion);
                break;
            case Key.Enter when _popup.IsOpen:
                e.Handled = true;
                PickHighlighted();
                break;
            case Key.Escape when _popup.IsOpen:
                e.Handled = true;
                ClosePopup();
                break;
            case Key.Tab:
                CancelSearch();
                ClosePopup();
                break;
        }
    }

    private void StartSearch(bool immediate)
    {
        CancelSearch();
        var text = _textBox.Text;
        if (string.IsNullOrWhiteSpace(text) || Search is null)
        {
            ClosePopup();
            return;
        }
        var cts = _searchCts = new CancellationTokenSource();
        var version = ++_searchVersion;
        _searching = true;
        _pendingSearch = RunSearchAsync(text, immediate ? TimeSpan.Zero : SearchDelay, version, cts.Token);
    }

    private async Task RunSearchAsync(string text, TimeSpan delay, int version, CancellationToken ct)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            var results = (await Search!(text, ct)).ToList();
            if (ct.IsCancellationRequested || version != _searchVersion) return;
            ShowResults(results);
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
        finally
        {
            if (version == _searchVersion) _searching = false;
        }
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _searchVersion++;
        _searching = false;
    }

    private void ShowResults(List<object> results)
    {
        _list.ItemsSource = results;
        _list.SelectedIndex = results.Count > 0 ? 0 : -1;
        _list.IsVisible = results.Count > 0;
        _noMatch.IsVisible = results.Count == 0;
        if (!_textBox.IsKeyboardFocusWithin)
        {
            ClosePopup();
            return;
        }
        _dropDown.MinWidth = Math.Max(_textBox.Bounds.Width, MinDropDownWidth);
        _popup.IsOpen = true;
        if (results.Count > 0) _list.ScrollIntoView(0);
    }

    private void MoveHighlight(int delta)
    {
        var count = _list.ItemCount;
        if (count == 0) return;
        var index = Math.Clamp(_list.SelectedIndex + delta, 0, count - 1);
        _list.SelectedIndex = index;
        _list.ScrollIntoView(index);
    }

    private async Task PickWhenSearchCompletesAsync(int version)
    {
        await _pendingSearch;
        // Ignore this Enter if the user typed more in the meantime (a newer search is running).
        if (version != _searchVersion || _searching) return;
        if (_popup.IsOpen) PickHighlighted();
    }

    private void PickHighlighted()
    {
        if (_list.SelectedItem is { } item) Pick(item);
    }

    private void Pick(object item)
    {
        CancelSearch();
        ClosePopup();
        ItemPicked?.Invoke(this, new ItemPickedEventArgs(item));
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var container = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (container?.DataContext is not { } item) return;
        e.Handled = true;
        Pick(item);
    }

    private void CloseIfFocusLeft()
    {
        if (_textBox.IsKeyboardFocusWithin || _dropDown.IsPointerOver) return;
        CancelSearch();
        ClosePopup();
    }

    private void ClosePopup()
    {
        if (_popup.IsOpen) _popup.IsOpen = false;
    }
}
