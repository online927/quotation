using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

/// <summary>
/// Tally-style keyboard flow: Enter moves to the next field; in the line entry
/// Product → Quantity → Rate → Disc % → Enter adds the line and returns to Product.
/// </summary>
public partial class QuotationEditorView : UserControl
{
    public QuotationEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        ProductBox.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0) FocusLater(QtyBox);
        };
        CustomerBox.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0) FocusLater(ProductBox);
        };
    }

    private QuotationEditorViewModel? Vm => DataContext as QuotationEditorViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Vm?.CustomerId is null) CustomerBox.Focus();
        else ProductBox.Focus();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Vm?.EditingLine is not null)
        {
            Vm.CancelEntryCommand.Execute(null);
            ProductBox.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Source is not Control source) return;

        // Let open drop-downs and multi-line boxes handle Enter themselves.
        if (source.FindAncestorOfType<AutoCompleteBox>(includeSelf: true) is { IsDropDownOpen: true }) return;
        if (source is TextBox { AcceptsReturn: true }) return;

        var owner = source.FindAncestorOfType<NumericUpDown>(includeSelf: true) as Control
                    ?? source.FindAncestorOfType<AutoCompleteBox>(includeSelf: true) as Control
                    ?? source;

        if (owner == DiscountBox || owner == DueOnBox)
        {
            // Commit pending text in the numeric box before adding the line.
            if (owner is NumericUpDown nud && nud.Text is { } t && decimal.TryParse(t, out var v)) nud.Value = v;
            Vm?.CommitEntryCommand.Execute(null);
            FocusLater(ProductBox);
            e.Handled = true;
            return;
        }
        if (owner is NumericUpDown n && n.Text is { } text && decimal.TryParse(text, out var value)) n.Value = value;

        var next = KeyboardNavigationHandler.GetNext(owner, NavigationDirection.Next);
        if (next is Control c)
        {
            c.Focus();
            e.Handled = true;
        }
    }

    private static void FocusLater(Control target) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => target.Focus(), Avalonia.Threading.DispatcherPriority.Input);
}
