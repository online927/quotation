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
        Quotation.Desktop.Services.ThemeService.ColoursChanged += OnThemeColoursChanged;
        OnThemeColoursChanged();
    }

    /// <summary>The Tally theme changes more than colours (PhoneStyles.axaml, ".tally"): square boxes, dark heading bars, yellow choices.</summary>
    private void OnThemeColoursChanged() =>
        Classes.Set("tally", Quotation.Desktop.Services.ThemeService.Current == Quotation.Contracts.AppTheme.Tally);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBackRequested;
        Quotation.Desktop.Services.ThemeService.ColoursChanged -= OnThemeColoursChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.GoBack()) e.Handled = true;
    }
}
