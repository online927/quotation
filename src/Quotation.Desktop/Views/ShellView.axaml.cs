using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    public ShellView()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Ctrl+F"),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => GlobalSearchBox.Focus()),
        });
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
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
    private TopLevel? _topLevel;

    // Keys are watched on the window, so Back works even when no field has the keyboard focus.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyDownEvent, OnKeyDownUnhandled, RoutingStrategies.Bubble);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDownUnhandled);
        _topLevel = null;
    }

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
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
        switch (e.Key, e.KeyModifiers)
        {
            // Esc that no field, list or popup used: go back.
            case (Key.Escape, KeyModifiers.None):
            case (Key.Left, KeyModifiers.Alt):
                vm.GoBack();
                break;
            case (Key.Right, KeyModifiers.Alt):
                vm.GoForward();
                break;
            default:
                return;
        }
        e.Handled = true;
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
