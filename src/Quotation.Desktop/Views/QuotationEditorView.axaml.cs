using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Desktop.Controls;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

/// <summary>
/// Tally-style keyboard flow: customer → Other references → Enter → items. Enter moves to the next field; in the line entry
/// Product → Quantity → Rate → Disc % → Due on → Description → Enter adds the line and returns to Product
/// (Shift+Enter starts a new line inside the description).
/// In the customer and product boxes the first match is highlighted, ↑/↓ choose and Enter accepts.
/// </summary>
public partial class QuotationEditorView : UserControl
{
    public QuotationEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        ProductBox.ItemPicked += (_, e) =>
        {
            if (e.Item is not ProductSearchHitDto hit || Vm is not { } vm) return;
            vm.SelectProduct(hit.Product);
            FocusLater(QtyBox);
        };
        CustomerBox.ItemPicked += async (_, e) =>
        {
            if (e.Item is not CustomerSearchHitDto hit || Vm is not { } vm) return;
            // After the party comes the reference (Other references), then the items.
            if (await vm.PickCustomerAsync(hit.Customer.Id)) FocusLater(OtherRefBox);
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
        if (e.Source is not Control source) return;
        var picker = source.FindAncestorOfType<SearchPicker>(includeSelf: true);

        if (e.Key == Key.Escape)
        {
            // An open list closes first; the next Esc cancels editing of a line.
            if (picker is { IsDropDownOpen: true } || Vm?.EditingLine is null) return;
            Vm.CancelEntryCommand.Execute(null);
            ProductBox.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;

        Control owner;
        if (picker is not null)
        {
            // The box accepts the highlighted match itself.
            if (picker.HandlesEnter) return;
            if (picker == ProductBox && Vm is { EntryProduct: null } vm && !string.IsNullOrWhiteSpace(vm.ProductSearchText))
            {
                // Text typed but no product chosen: show the matches again instead of moving on.
                picker.SearchNow();
                e.Handled = true;
                return;
            }
            owner = picker.InputBox;
        }
        else
        {
            if (source == DescriptionBox)
            {
                // Last field of the line: Enter adds the line (Shift+Enter is a new line in the description).
                Vm?.CommitEntryCommand.Execute(null);
                FocusLater(ProductBox);
                e.Handled = true;
                return;
            }
            // Let other multi-line boxes (remarks, terms …) handle Enter themselves.
            if (source is TextBox { AcceptsReturn: true }) return;
            owner = source.FindAncestorOfType<NumericUpDown>(includeSelf: true) as Control ?? source;
        }

        if (owner == OtherRefBox)
        {
            // The other reference boxes are reached with Tab or the mouse; Enter goes on to the items.
            ProductBox.Focus();
            e.Handled = true;
            return;
        }
        if (owner == DueOnBox)
        {
            DescriptionBox.Focus();
            DescriptionBox.CaretIndex = DescriptionBox.Text?.Length ?? 0;
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
