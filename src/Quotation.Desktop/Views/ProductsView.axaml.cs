using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && ResultList.ItemCount > 0)
            {
                ResultList.Focus();
                e.Handled = true;
            }
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }
}
