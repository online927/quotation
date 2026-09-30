using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class QuotationListView : UserControl
{
    public QuotationListView()
    {
        InitializeComponent();
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && List.ItemCount > 0)
            {
                List.SelectedIndex = Math.Max(0, List.SelectedIndex);
                List.Focus();
                e.Handled = true;
            }
        };
        List.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is QuotationListViewModel vm)
            {
                vm.OpenCommand.Execute(null);
                e.Handled = true;
            }
        };
        List.DoubleTapped += (_, _) => (DataContext as QuotationListViewModel)?.OpenCommand.Execute(null);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }
}
