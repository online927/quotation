using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Quotation.Mobile.ViewModels;

namespace Quotation.Mobile.Views;

public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    /// <summary>The phone's Back button: the page before (on a bottom-menu page, Home; on Home the phone closes the app).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBackRequested;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.GoBack()) e.Handled = true;
    }
}
