using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
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
