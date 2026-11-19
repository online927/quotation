using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

/// <summary>
/// Back like a web browser: the ‹ › buttons, Alt+← / Alt+→, the mouse's back/forward side buttons, and Esc
/// (when the page itself has nothing to close). Leaving unsaved work asks Save / Don't Save / Cancel first.
/// </summary>
public partial class ShellView : UserControl
{
    private ShellViewModel? _subscribed;

    /// <summary>The chosen page in the side menu is always in view (on a small screen it hid under "Hide menu").</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnShellChanged;
        _subscribed = DataContext as ShellViewModel;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnShellChanged;
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.CurrentPage)) return;
        Dispatcher.UIThread.Post(ShowChosenPage, DispatcherPriority.Loaded);
    }

    public void ShowChosenPage()
    {
        var chosen = NavScroller.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("nav") && b.Classes.Contains("selected"));
        chosen?.BringIntoView();
    }

    public ShellView()
    {
        InitializeComponent();
        if (MacSupport.IsMac)
        {
            // The shortcuts card shows the Mac keys.
            foreach (var t in ShortcutsPanel.GetLogicalDescendants().OfType<TextBlock>())
                t.Text = t.Text switch { "Ctrl" => "⌘", "Alt" => "⌥", _ => t.Text };
        }
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Ctrl+F"),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => GlobalSearchBox.Focus()),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("F1"),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => Vm?.ToggleShortcutsCommand.Execute(null)),
        });
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        // Top search: a typed number shows its quotation(s) below the box; ↑ / ↓ pick one, Enter opens it, Esc closes the list.
        GlobalSearchBox.GotFocus += (_, _) => { if (Vm is { } vm) vm.IsSearchFocused = true; };
        GlobalSearchBox.LostFocus += (_, _) =>
            DispatcherTimer.RunOnce(() => { if (Vm is { } vm && !GlobalSearchBox.IsFocused) vm.IsSearchFocused = false; }, TimeSpan.FromMilliseconds(200));
        GlobalSearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        GlobalSearchBox.PropertyChanged += (_, e) =>
        {
            // Typing again after Esc shows the list again.
            if (e.Property == TextBox.TextProperty && GlobalSearchBox.IsFocused && Vm is { } vm) vm.IsSearchFocused = true;
        };
        DialogPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (DialogPanel.IsVisible)
            {
                _focusBeforeDialog = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
                Dispatcher.UIThread.Post(() =>
                {
                    if (Vm?.PendingDialog is { HasInput: true }) DialogInput.Focus();
                    else DialogPrimary.Focus();
                }, DispatcherPriority.Input);
            }
            else if (_focusBeforeDialog is { } previous)
            {
                _focusBeforeDialog = null;
                Dispatcher.UIThread.Post(() => { if (previous.IsAttachedToVisualTree() && previous.IsEffectivelyVisible) previous.Focus(); },
                    DispatcherPriority.Input);
            }
        };
        PromptPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (PromptPanel.IsVisible)
            {
                _focusBeforePrompt = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
                Dispatcher.UIThread.Post(() => PromptSave.Focus(), DispatcherPriority.Input);
            }
            else if (_focusBeforePrompt is { } previous)
            {
                // Cancelled: back to the field the user was in.
                _focusBeforePrompt = null;
                Dispatcher.UIThread.Post(() => { if (previous.IsAttachedToVisualTree() && previous.IsEffectivelyVisible) previous.Focus(); },
                    DispatcherPriority.Input);
            }
        };
    }

    private Control? _focusBeforePrompt;
    private Control? _focusBeforeDialog;
    private TopLevel? _topLevel;

    // Keys are watched on the window, so Back works even when no field has the keyboard focus.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyDownEvent, OnKeyDownUnhandled, RoutingStrategies.Bubble);
        ThemeService.ColoursChanged += OnThemeColoursChanged;
        OnThemeColoursChanged();
    }

    /// <summary>The Tally theme changes more than colours (Styles/TallyStyles.axaml): its styles look for the "tally" class here.</summary>
    private void OnThemeColoursChanged() => Classes.Set("tally", IsTallyTheme);

    private static bool IsTallyTheme => ThemeService.Shown == Quotation.Contracts.AppTheme.Tally; // the look on screen (not on festival days)

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDownUnhandled);
        _topLevel = null;
        ThemeService.ColoursChanged -= OnThemeColoursChanged;
    }

    // ===== Tally theme: the key bar and the menu's letters =====

    private void OnKeyBarClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is KeyBarItem key) PressKey(key.Key, key.Modifiers);
    }

    private void OnEscKeyClick(object? sender, RoutedEventArgs e) => PressKey(Key.Escape, KeyModifiers.None);

    /// <summary>
    /// A key of the key bar works as if it was pressed: on the box that has the keyboard focus (the key bar never takes it),
    /// or else on the page. A shortcut of the page or the window runs its command; other keys go to the page as usual.
    /// </summary>
    public void PressKey(Key key, KeyModifiers modifiers)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var target = focused is not null && this.IsVisualAncestorOf(focused) && !KeyBarPanel.IsVisualAncestorOf(focused)
            ? focused
            : PageHost.GetVisualDescendants().OfType<UserControl>().FirstOrDefault() as Control ?? this;
        var args = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target };
        foreach (var element in target.GetSelfAndVisualAncestors().OfType<InputElement>())
        {
            foreach (var binding in element.KeyBindings.ToList())
            {
                if (binding.Gesture?.Matches(args) != true || binding.Command is not { } command || !command.CanExecute(binding.CommandParameter)) continue;
                command.Execute(binding.CommandParameter);
                return;
            }
        }
        target.RaiseEvent(args);
        if (!args.Handled && key == Key.Escape) Vm?.GoBack();
    }

    /// <summary>Tally theme: on the Dashboard (or in the menu) a letter opens the page whose letter it is (red in the menu).</summary>
    private bool OpenByLetter(KeyEventArgs e)
    {
        if (!IsTallyTheme || e.KeyModifiers != KeyModifiers.None || e.Key < Key.A || e.Key > Key.Z || Vm is not { } vm) return false;
        if (vm.PendingDialog is not null || vm.PendingPrompt is not null || vm.ShowShortcuts) return false;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is TextBox || focused?.FindAncestorOfType<TextBox>() is not null) return false;
        var inMenu = focused is not null && Sidebar.IsVisualAncestorOf(focused);
        if (vm.CurrentPage is not DashboardViewModel && !inMenu) return false;
        var letter = (char)('A' + (e.Key - Key.A));
        var item = vm.NavItems.FirstOrDefault(n => n.HotKey == letter);
        if (item is null) return false;
        vm.NavigateCommand.Execute(item.Key);
        return true;
    }

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.X && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift) && Vm is { } game)
        {
            game.OpenXoCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (Vm is { ShowShortcuts: true } help)
        {
            // The shortcuts card: Esc or F1 closes it.
            if (e.Key is Key.Escape or Key.F1)
            {
                help.ShowShortcuts = false;
                e.Handled = true;
            }
            return;
        }
        if (Vm is { XoGame: not null } playing)
        {
            // While the game is open, Esc closes it; other keys do not reach the page behind.
            if (e.Key == Key.Escape) playing.CloseXoCommand.Execute(null);
            if (e.Key is not (Key.Tab or Key.Space or Key.Enter)) e.Handled = true;
            return;
        }
        if (Vm is { PendingDialog: { } dialog })
        {
            // Enter = the main answer (unless a button has the focus), Esc = cancel; nothing reaches the page behind.
            var onButton = e.Source is Button b && DialogPanel.IsVisualAncestorOf(b);
            if (e.Key == Key.Escape) dialog.Choose(false);
            else if (e.Key == Key.Enter && !onButton) dialog.Choose(true);
            // A Yes / No question (Tally theme: "Approve? Yes or No"): Y and N answer it.
            else if (!dialog.HasInput && e.KeyModifiers == KeyModifiers.None && e.Key == Key.Y && dialog.PrimaryText == "Yes") dialog.Choose(true);
            else if (!dialog.HasInput && e.KeyModifiers == KeyModifiers.None && e.Key == Key.N && dialog.SecondaryText == "No") dialog.Choose(false);
            else if (e.Source is Visual inDialog && DialogPanel.IsVisualAncestorOf(inDialog)) return; // typing in the box, Tab, Space on a button
            e.Handled = true;
            return;
        }
        // Alt+← / Alt+→ are never typing keys: Back / Forward before any box sees them.
        if (Vm is { PendingPrompt: null } shell && e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
        {
            if (e.Key == Key.Left) shell.GoBack();
            else shell.GoForward();
            e.Handled = true;
            return;
        }
        if (Vm is not { PendingPrompt: { } prompt }) return;
        // While the question is shown, keys answer it and never reach the page behind.
        var onPromptButton = e.Source is Visual v && PromptPanel.IsVisualAncestorOf(v);
        switch (e.Key)
        {
            case Key.Escape: prompt.Choose(LeaveChoice.Cancel); break;
            case Key.N or Key.D: prompt.Choose(LeaveChoice.DontSave); break;
            case Key.Y or Key.S: prompt.Choose(LeaveChoice.Save); break;
            case Key.Enter or Key.Space when onPromptButton: return; // the focused button answers
            case Key.Enter: prompt.Choose(LeaveChoice.Save); break;
            case Key.Tab or Key.Left or Key.Right when onPromptButton: return;
        }
        e.Handled = true;
    }

    private void OnKeyDownUnhandled(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Vm is not { } vm) return;
        if (OpenByLetter(e))
        {
            e.Handled = true;
            return;
        }
        switch (e.Key, e.KeyModifiers)
        {
            // Esc that no field, list or popup used: go back.
            case (Key.Escape, KeyModifiers.None):
                vm.GoBack();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { ShowNumberMatches: true } vm) return;
        switch (e.Key)
        {
            case Key.Down:
                vm.MoveNumberHighlight(+1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveNumberHighlight(-1);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.IsSearchFocused = false; // the list closes; the text stays
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.GetCurrentPoint(this).Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.XButton1Pressed:
                vm.GoBack();
                e.Handled = true;
                break;
            case PointerUpdateKind.XButton2Pressed:
                vm.GoForward();
                e.Handled = true;
                break;
        }
    }
}
