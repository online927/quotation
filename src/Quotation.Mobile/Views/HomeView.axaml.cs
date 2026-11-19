using Avalonia;
using Avalonia.Controls;
using Quotation.Desktop.Services;
using Quotation.Mobile.ViewModels;

namespace Quotation.Mobile.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    // The festival greeting follows the look on the screen (it can come on after the page opened)
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

    private void OnLookChanged() => (DataContext as HomeViewModel)?.FestivalChanged();
}
