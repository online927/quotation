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
        // Round 39 (K4): F2 opens the date filter's calendar (Quotation History); ← → move the day, Enter shows it, Esc closes.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.F2 || e.KeyModifiers != KeyModifiers.None || !DateFilterBox.IsEffectivelyVisible) return;
            OpenDateFilter();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>F2: the date filter gets the keyboard and its calendar opens on the chosen day (today when none).</summary>
    public void OpenDateFilter()
    {
        DateFilterBox.DisplayDate = DateFilterBox.SelectedDate ?? DateTime.Today;
        DateFilterBox.Focus(NavigationMethod.Tab);
        DateFilterBox.IsDropDownOpen = true;
    }

    private void OnClearDate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QuotationListViewModel vm) vm.DateFilter = null;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }
}
