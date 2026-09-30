using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Views;

public partial class AiInboxView : UserControl
{
    public AiInboxView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        RequestBox.Focus();
    }
}
