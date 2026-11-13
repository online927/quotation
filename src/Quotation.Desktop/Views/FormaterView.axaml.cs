using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Views;

public partial class FormaterView : UserControl
{
    public FormaterView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FormaterTextBox.Focus();
    }
}
