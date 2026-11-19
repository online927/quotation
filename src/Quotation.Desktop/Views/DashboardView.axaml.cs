using Avalonia;
using Avalonia.Controls;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    // The festival greeting follows the look on the screens (it can come on after the page opened)
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeService.ColoursChanged += OnLookChanged;
        OnLookChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeService.ColoursChanged -= OnLookChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLookChanged() => (DataContext as DashboardViewModel)?.FestivalChanged();
}
