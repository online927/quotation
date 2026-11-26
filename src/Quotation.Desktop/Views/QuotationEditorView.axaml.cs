using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Desktop.Controls;
using Quotation.Desktop.ViewModels;

using Quotation.Desktop.Services;

namespace Quotation.Desktop.Views;

/// <summary>
/// Tally-style keyboard flow: customer → Other references → Enter → items. Enter moves to the next field; in the line entry
/// Product → Quantity → Rate → Disc % → Description → Enter adds the line and returns to Product
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
        // Edit on a line: the cursor goes to its Quantity box (never stays on the line's buttons).
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(QuotationEditorViewModel.EditingLine) && vm.InListEditing)
                {
                    // Round 39 (IE-1): in the list itself: the cursor in the line's Qty (or the gear's first column).
                    FocusInline(InlineName(vm.StepColumns.FirstOrDefault()) ?? "InlineQtyBox");
                    return;
                }
                if (e.PropertyName != nameof(QuotationEditorViewModel.EditingLine) || vm.EditingLine is null) return;
                // With the gear on columns, the cursor goes straight to the first chosen column's box.
                if (vm.StepColumns.FirstOrDefault() is { } column) OnFocusEntry(column);
                else FocusLater(QtyBox);
            };
            // After a line is saved: a new line (Product), or with the gear on the next line in the chosen box.
            vm.FocusEntry += OnFocusEntry;
        };
        // A list pasted into the Product box opens the pasted-list box; a single line is typed in as usual.
        ProductBox.InputBox.AddHandler(TextBox.PastingFromClipboardEvent, OnProductPasting, RoutingStrategies.Bubble);
        RateBox.AddHandler(TextBox.PastingFromClipboardEvent, OnRatePasting, RoutingStrategies.Bubble);
        QtyBox.AddHandler(TextBox.PastingFromClipboardEvent, OnQtyPasting, RoutingStrategies.Bubble);
        DiscountBox.AddHandler(TextBox.PastingFromClipboardEvent, OnDiscountPasting, RoutingStrategies.Bubble);
        DescriptionBox.AddHandler(TextBox.PastingFromClipboardEvent, OnDescriptionPasting, RoutingStrategies.Bubble);
        DeliveryBox.AddHandler(TextBox.PastingFromClipboardEvent, OnDeliveryPasting, RoutingStrategies.Bubble);
        LinesList.PointerMoved += OnLinesPointerMoved;
        // Round 39 (IE-1): the item search inside a row (changing a line in the list) picks another Tally item.
        LinesList.ContainerPrepared += (_, e) =>
        {
            if (e.Container.GetVisualDescendants().OfType<SearchPicker>().FirstOrDefault(p => p.Name == "InlineProductBox") is { } inline)
            {
                Wire(inline);
                WireDelivery(e.Container);
            }
            else e.Container.Loaded += (_, _) =>
            {
                if (e.Container.GetVisualDescendants().OfType<SearchPicker>().FirstOrDefault(p => p.Name == "InlineProductBox") is { } later) Wire(later);
                WireDelivery(e.Container);
            };
        };
        LinesList.PointerReleased += (_, _) => EndDrag();
        LinesList.PointerCaptureLost += (_, _) => EndDrag();
        CustomerBox.ItemPicked += async (_, e) =>
        {
            if (e.Item is not CustomerSearchHitDto hit || Vm is not { } vm) return;
            // After the party comes the reference (Other references), then the items.
            if (await vm.PickCustomerAsync(hit.Customer.Id)) FocusLater(OtherRefBox);
        };
    }

    private QuotationEditorViewModel? Vm => DataContext as QuotationEditorViewModel;

    private readonly HashSet<SearchPicker> _wired = [];

    /// <summary>Round 39 (IE-1): a picked item replaces the line's item; the typed values stay.</summary>
    private void Wire(SearchPicker picker)
    {
        if (!_wired.Add(picker)) return;
        picker.ItemPicked += (_, e) =>
        {
            if (e.Item is not ProductSearchHitDto hit || Vm is not { } vm) return;
            vm.ChangeEditingItem(hit.Product);
            FocusInline("InlineQtyBox");
        };
    }

    /// <summary>Round 39 (IE-1): a box of the line being changed in the list ("InlineQtyBox" …), focused with its text selected.</summary>
    private bool FocusInline(string name)
    {
        if (Vm is not { InListEditing: true, EditingLine: { } line } vm) return false;
        var at = vm.Lines.IndexOf(line);
        Dispatcher.UIThread.Post(() =>
        {
            if (LinesList.ContainerFromIndex(at) is not { } row) return;
            var target = row.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
            if (target is null) return;
            target.BringIntoView();
            if (target is SearchPicker picker) { picker.InputBox.Focus(NavigationMethod.Tab); picker.InputBox.SelectAll(); return; }
            var input = target as TextBox ?? target.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (input is null) { target.Focus(); return; }
            input.Focus(NavigationMethod.Tab);
            input.SelectAll();
        }, DispatcherPriority.Background);
        return true;
    }

    private bool _rowsLookApplied;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (!_rowsLookApplied && Vm is { } page)
        {
            _rowsLookApplied = true;
            ItemRowsLook.Apply(this, LinesList, page.BiggerItemRows);
        }
        if (Vm?.CustomerId is null) CustomerBox.Focus();
        else ProductBox.Focus();
    }

    /// <summary>Insert or Alt+C: the cursor in the Customer box (the buyer box opens first when it is folded).</summary>
    /// <summary>A delivery time picked from the list: close it and go back to typing (Round 35).</summary>
    /// <summary>
    /// A delivery time picked in the list. The list lives in a pop-up outside this page, so the page's command cannot be
    /// reached from it by binding: the choice is handed to the page here.
    /// </summary>
    private void OnDeliveryPicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Content: string choice } && Vm is { } vm) vm.PickDeliveryCommand.Execute(choice);
        DeliveryPickButton.Flyout?.Hide();
        DeliveryBox.Focus();
        DeliveryBox.CaretIndex = DeliveryBox.Text?.Length ?? 0;
    }

    /// <summary>Round 39 (Z8): Enter adds the note, Shift+Enter starts a new line in it, Esc leaves the box.</summary>
    private void OnNewNoteKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || Vm is not { } vm) return;
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var at = box.CaretIndex;
            box.Text = (box.Text ?? "").Insert(at, "\n");
            box.CaretIndex = at + 1;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.AddNoteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Focus();
            e.Handled = true;
        }
    }

    /// <summary>The ✕ at the top of the gear menu closes it (Round 35).</summary>
    private void OnStepMenuClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => StepGearButton.Flyout?.Hide();

    public void FocusCustomerBox()
    {
        if (Vm is { HeaderCollapsed: true } folded)
        {
            folded.HeaderCollapsed = false;
            Dispatcher.UIThread.Post(() => FocusPicker(CustomerBox), DispatcherPriority.Loaded);
            return;
        }
        FocusPicker(CustomerBox);
    }

    private static void FocusPicker(SearchPicker box)
    {
        if (!box.IsEffectivelyEnabled || !box.IsEffectivelyVisible) return; // e.g. an approved quotation
        box.BringIntoView();
        box.InputBox.Focus(NavigationMethod.Tab);
        box.InputBox.SelectAll();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Control source) return;
        if (e.Key == Key.P && AltKey.IsAlt(e.KeyModifiers))
        {
            // Alt+P (either Alt key): straight to Packing & Forwarding, its amount selected to type over.
            FocusPacking();
            AltKey.SwallowTypedLetter(this, e.KeyModifiers);
            e.Handled = true;
            return;
        }
        // Round 39 (Z8): Alt+N opens the notes and puts the cursor in "Write a note…".
        if (e.Key == Key.N && AltKey.IsAlt(e.KeyModifiers) && Vm is { NotesOn: true } noting)
        {
            AltKey.SwallowTypedLetter(this, e.KeyModifiers);
            noting.OpenNotesCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Round 39 (K5): Alt+D removes the highlighted line (with Undo).
        if (e.Key == Key.D && AltKey.IsAlt(e.KeyModifiers) && Vm is { } deleting)
        {
            AltKey.SwallowTypedLetter(this, e.KeyModifiers);
            if (deleting.RemovePickedLine()) ScrollToHighlight();
            e.Handled = true;
            return;
        }
        // Alt+C: the Customer box (also Insert, anywhere in the window); Alt+I: the Product box to add an item (either Alt key).
        if (AltKey.IsAlt(e.KeyModifiers) && e.Key is Key.C or Key.I)
        {
            AltKey.SwallowTypedLetter(this, e.KeyModifiers);
            if (e.Key == Key.C) FocusCustomerBox();
            else FocusPicker(ProductBox);
            e.Handled = true;
            return;
        }
        // Round 40 (ED-1): Home (outside a typing box) goes to the Product box to add an item.
        if (e.Key == Key.Home && e.KeyModifiers == KeyModifiers.None && source is not TextBox && source.FindAncestorOfType<TextBox>() is null
            && source.FindAncestorOfType<SearchPicker>(includeSelf: true) is null && Vm is { IsPasteOpen: false, IsInvoiceOpen: false, EditingLine: null })
        {
            FocusPicker(ProductBox);
            e.Handled = true;
            return;
        }
        // Round 38 (Q5): Ctrl+D copies the picked line just below; Ctrl+↑ / Ctrl+↓ move it (⌘ on a Mac).
        if (e.KeyModifiers == MacSupport.Command && e.Key is Key.D or Key.Up or Key.Down && Vm is { } lines
            && source.FindAncestorOfType<SearchPicker>(includeSelf: true) is not { IsDropDownOpen: true })
        {
            if (e.Key == Key.D) lines.DuplicateLineCommand.Execute(null);
            else lines.MoveLineStep(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }
        var picker = source.FindAncestorOfType<SearchPicker>(includeSelf: true);

        // Round 39 (IE-1): inside a line changed in the list: Enter updates it (Shift+Enter is a new line in its description),
        // Shift+Tab from Qty opens the item search.
        if (source.FindAncestorOfType<Grid>(includeSelf: true) is { } host && host.FindAncestorOfType<Grid>(includeSelf: true) is not null
            && source.GetSelfAndVisualAncestors().OfType<Grid>().Any(g => g.Classes.Contains("inlineedit")) && Vm is { InListEditing: true } inlineVm)
        {
            var inPicker = source.FindAncestorOfType<SearchPicker>(includeSelf: true);
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && inPicker is null)
            {
                inlineVm.SaveLineCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift
                && source.FindAncestorOfType<NumericUpDown>(includeSelf: true) is { Name: "InlineQtyBox" } && inlineVm.EditingLine is { } swap)
            {
                swap.IsChangingItem = true;
                FocusInline("InlineProductBox");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && inPicker is { IsDropDownOpen: false } && inlineVm.EditingLine is { IsChangingItem: true } keep)
            {
                keep.IsChangingItem = false; // the old item stays
                FocusInline("InlineQtyBox");
                e.Handled = true;
                return;
            }
        }

        // Round 39 (KL-1): outside the typing boxes ↓ / ↑ walk the item lines, Enter edits the highlighted one and Esc
        // first removes the highlight (only then does Esc go Back).
        var typing = source is TextBox || source.FindAncestorOfType<TextBox>() is not null || picker is not null;
        if (Vm is { IsPasteOpen: false, IsInvoiceOpen: false } walker && !typing && e.KeyModifiers == KeyModifiers.None
            && source.FindAncestorOfType<Button>(includeSelf: true) is null)
        {
            if (e.Key is Key.Down or Key.Up && walker.Lines.Count > 0)
            {
                walker.MoveHighlight(e.Key == Key.Down ? 1 : -1);
                ScrollToHighlight();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && walker.SelectedLine is { } chosen && walker.EditingLine is null && walker.CanEditContent)
            {
                walker.EditLineCommand.Execute(chosen);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && walker.EditingLine is null && walker.SelectedLine is not null)
            {
                walker.SelectedLine = null;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape && Vm is { IsInvoiceOpen: true } invoices)
        {
            invoices.CloseInvoicesCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && Vm is { IsPasteOpen: true } pasting)
        {
            if (picker is { IsDropDownOpen: true }) return;
            pasting.ClosePasteListCommand.Execute(null);
            ProductBox.Focus();
            e.Handled = true;
            return;
        }
        if (Vm is { IsPasteOpen: true } && e.Key == Key.Enter && PasteOverlay.IsVisualAncestorOf(source))
        {
            // Inside the pasted-list box Enter belongs to its own controls (a search box, a list, the Add button).
            return;
        }
        if (e.Key == Key.Escape)
        {
            // An open list closes first; the next Esc cancels editing of a line.
            if (picker is { IsDropDownOpen: true }) return;
            if (Vm?.EditingLine is { } edited)
            {
                Vm.CancelEntryCommand.Execute(null);
                Vm.SelectedLine = edited; // Round 39 (KL-1): the line stays highlighted; ↓ / ↑ go on from it
                Focus();
                e.Handled = true;
                return;
            }
            // Round 39 (KL-1): Esc in an empty entry box leaves it, so ↓ / ↑ can walk the lines; the next Esc goes Back.
            if (Vm is { Lines.Count: > 0, EntryProduct: null } && typing && string.IsNullOrEmpty(Vm.ProductSearchText)
                && (picker == ProductBox || EntryBoxes().Contains(source.FindAncestorOfType<TextBox>(includeSelf: true))))
            {
                Focus();
                e.Handled = true;
            }
            return;
        }
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;

        Control owner;
        if (picker is not null)
        {
            // The box accepts the highlighted match itself.
            if (picker.HandlesEnter) return;
            if (picker == ProductBox && Vm is { EntryProduct: null, EditingLine: null } done && string.IsNullOrWhiteSpace(done.ProductSearchText)
                && done.Lines.Count > 0 && !picker.IsDropDownOpen)
            {
                // Round 39 (K2): Enter on an empty Product box = done with the items: P&F → Remarks → Approve.
                if (!FocusPackingStep()) FocusApprove();
                e.Handled = true;
                return;
            }
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
                // Last field of the line: Enter adds the line (Shift+Enter is a new line in the description); with the gear
                // on, a changed line opens the next one.
                Vm?.SaveLineCommand.Execute(null);
                e.Handled = true;
                return;
            }
            // Round 39 (K2): Enter in Remarks goes on to Approve (Shift+Enter is a new line in it).
            if (source == RemarksBox && FocusApprove())
            {
                e.Handled = true;
                return;
            }
            // Let other multi-line boxes (remarks, terms …) handle Enter themselves.
            if (source is TextBox { AcceptsReturn: true }) return;
            // A button with the cursor on it (Approve after Enter in P&F, Save as PDF …): Enter presses it.
            if (source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
            owner = source.FindAncestorOfType<NumericUpDown>(includeSelf: true) as Control ?? source;
        }

        if (owner == OtherRefBox)
        {
            // The other reference boxes are reached with Tab or the mouse; Enter folds the header and goes on to the items.
            Vm?.FoldHeader();
            ProductBox.Focus();
            e.Handled = true;
            return;
        }
        if (owner is NumericUpDown n && n.Text is { } text && decimal.TryParse(text, out var value)) n.Value = value;
        // Gear on columns: Enter in a chosen box goes to the next chosen box of the line; after the last one it saves the
        // line and opens the next line in the first chosen box.
        if (Vm is { EditingLine: not null } stepping &&
            stepping.StepColumns.FirstOrDefault(k => StepBox(k) == owner ||
                (k == "Name" && owner == ProductBox.InputBox && stepping.EntryProduct is not null)) is { } column)
        {
            if (stepping.NextStepColumn(column) is { } nextColumn) OnFocusEntry(nextColumn);
            else stepping.SaveLineCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (owner == DiscountBox)
        {
            DescriptionBox.Focus();
            DescriptionBox.CaretIndex = DescriptionBox.Text?.Length ?? 0;
            e.Handled = true;
            return;
        }
        if (owner == PackingBox && (FocusRemarks() || FocusApprove()))
        {
            e.Handled = true;
            return;
        }

        var next = KeyboardNavigationHandler.GetNext(owner, NavigationDirection.Next);
        if (next is Control c)
        {
            c.Focus();
            e.Handled = true;
        }
    }

    /// <summary>The box of a gear column ("Rate" → the Rate box); null for Off and the whole line.</summary>
    private Control? StepBox(string mode) => mode switch
    {
        "Name" => ProductBox,
        "Description" => DescriptionBox,
        "Quantity" => QtyBox,
        "Rate" => RateBox,
        "Discount" => DiscountBox,
        _ => null,
    };

    /// <summary>After a line is saved: the cursor in the box the gear asks for (the whole line: Quantity), or a new line.</summary>
    private static string? InlineName(string? column) => column switch
    {
        "Name" => "InlineProductBox",
        "Description" => "InlineDescriptionBox",
        "Quantity" => "InlineQtyBox",
        "Rate" => "InlineRateBox",
        "Discount" => "InlineDiscountBox",
        _ => null,
    };

    private void OnFocusEntry(string box)
    {
        if (box == "Line")
        {
            // Round 40 (ED-1): back on the line that was changed; the page does not move to the Product box.
            Dispatcher.UIThread.Post(() =>
            {
                Focus();
                ScrollToHighlight();
            }, DispatcherPriority.Background);
            return;
        }
        if (Vm is { InListEditing: true } listed && box != "Product")
        {
            if (box == "Name" && listed.EditingLine is { } changing) changing.IsChangingItem = true;
            FocusInline(InlineName(box) ?? "InlineQtyBox");
            return;
        }
        Control target = box == "Product" ? ProductBox : StepBox(box) ?? QtyBox;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (target == ProductBox)
            {
                ProductBox.InputBox.Focus();
                return;
            }
            target.BringIntoView();
            var input = target as TextBox ?? target.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (input is null)
            {
                target.Focus();
                return;
            }
            input.Focus(NavigationMethod.Tab);
            input.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>The line entry boxes (Quantity, Rate, Disc. %, Description), for Esc (Round 39).</summary>
    private IEnumerable<TextBox?> EntryBoxes() =>
        new Control[] { QtyBox, RateBox, DiscountBox, DescriptionBox }.Select(c => c as TextBox ?? c.GetVisualDescendants().OfType<TextBox>().FirstOrDefault());

    /// <summary>The highlighted line is scrolled into view (Round 39, KL-1).</summary>
    private void ScrollToHighlight()
    {
        if (Vm?.SelectedLine is not { } line) return;
        var at = Vm.Lines.IndexOf(line);
        Dispatcher.UIThread.Post(() => LinesList.ContainerFromIndex(at)?.BringIntoView(), DispatcherPriority.Loaded);
    }

    /// <summary>Round 39 (K2): P&amp;F when it can be typed in; false otherwise.</summary>
    private bool FocusPackingStep()
    {
        if (!PackingBox.IsEffectivelyEnabled || !PackingBox.IsEffectivelyVisible) return false;
        FocusPacking();
        return true;
    }

    /// <summary>Round 39 (K2): after P&amp;F the cursor goes to Remarks (when the remarks are shown and can be changed).</summary>
    private bool FocusRemarks()
    {
        if (!RemarksBox.IsEffectivelyEnabled || !RemarksBox.IsEffectivelyVisible || RemarksBox.IsReadOnly) return false;
        RemarksBox.BringIntoView();
        RemarksBox.Focus(NavigationMethod.Tab);
        RemarksBox.CaretIndex = RemarksBox.Text?.Length ?? 0;
        return true;
    }

    /// <summary>Alt+P: the cursor in the Packing &amp; Forwarding box with its amount selected (when it can be changed).</summary>
    public void FocusPacking()
    {
        if (!PackingBox.IsEffectivelyEnabled || !PackingBox.IsEffectivelyVisible) return;
        PackingBox.BringIntoView();
        PackingBox.Focus(NavigationMethod.Tab);
        if (PackingBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
        {
            box.Focus(NavigationMethod.Tab);
            box.SelectAll();
        }
    }

    /// <summary>
    /// Enter in Packing &amp; Forwarding: the cursor goes to Approve (or Save as PDF when Approve is not available) without
    /// pressing it; Enter again approves. False when neither can be used.
    /// </summary>
    public bool FocusApprove()
    {
        var target = new[] { ApproveButton, SaveAsPdfButton }
            .FirstOrDefault(b => b is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true });
        if (target is null) return false;
        target.Focus(NavigationMethod.Tab); // keyboard focus: the button shows its focus ring
        return true;
    }

    private async void OnProductPasting(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanEditContent: true } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        e.Handled = true; // the text is put in below (one line) or goes to the pasted-list box (two or more)
        var box = ProductBox.InputBox;
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception) { return; }
        if (string.IsNullOrEmpty(text)) return;
        await PasteIntoProductAsync(vm, box, text);
    }

    /// <summary>Round 38 (PL-1): the small 🔗 after an item name.</summary>
    private void OnOpenProductLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: QuotationLineViewModel line } && Vm is { } vm) vm.OpenProductLink(line);
        e.Handled = true;
    }

    private void OnQtyPasting(object? sender, RoutedEventArgs e) => OnColumnPasting(e, PasteColumnKind.Quantity);
    private void OnDiscountPasting(object? sender, RoutedEventArgs e) => OnColumnPasting(e, PasteColumnKind.Discount);

    /// <summary>
    /// Round 39 (DX-1): Ctrl+V in Description: a description column copied from Excel (the clipboard carries a spreadsheet
    /// format besides the text, and 2+ cells) goes one cell per line; text from WhatsApp, Word or e-mail pastes as usual.
    /// </summary>
    private async void OnDescriptionPasting(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanEditContent: true, Lines.Count: > 0 } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string[] formats;
        try { formats = await clipboard.GetFormatsAsync(); }
        catch (Exception) { return; }
        if (!IsSpreadsheet(formats)) return; // a normal paste goes ahead
        e.Handled = true;
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception) { return; }
        if (string.IsNullOrEmpty(text)) return;
        if (!PasteDescriptions(vm, text) && e.Source is TextBox box)
        {
            var at = box.CaretIndex;
            box.Text = (box.Text ?? "").Insert(at, text);
            box.CaretIndex = at + text.Length;
        }
    }

    /// <summary>The clipboard came from a spreadsheet (Excel, LibreOffice): it has a sheet or HTML-table format besides text.</summary>
    public static bool IsSpreadsheet(IEnumerable<string> formats) => formats.Any(f =>
        f.Contains("XML Spreadsheet", StringComparison.OrdinalIgnoreCase) || f.StartsWith("Biff", StringComparison.OrdinalIgnoreCase)
        || f.Contains("HTML", StringComparison.OrdinalIgnoreCase) || f.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase)
        || f.Equals("text/html", StringComparison.OrdinalIgnoreCase));

    /// <summary>Spreads the cells of <paramref name="text"/> over the lines (also used by tests); false when it is not a column.</summary>
    public static bool PasteDescriptions(QuotationEditorViewModel vm, string text)
    {
        var cells = Quotation.Core.Import.ExcelColumnText.Cells(text);
        return cells.Count(c => c.Length > 0) >= 2 && vm.PasteDescriptionColumn(cells, text.Trim()) > 0;
    }

    /// <summary>Round 37: Ctrl+V in Qty / Disc %: a column from Excel fills the lines down; anything else pastes as usual.</summary>
    private async void OnColumnPasting(RoutedEventArgs e, PasteColumnKind kind)
    {
        if (Vm is not { CanEditContent: true } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (e.Source is not TextBox box) return;
        e.Handled = true;
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception) { return; }
        if (string.IsNullOrEmpty(text)) return;
        PasteIntoColumn(vm, box, text, kind);
    }

    /// <summary>Round 41 (PD-2): the Delivery box inside a row being changed in the list pastes a column too.</summary>
    private void WireDelivery(Control row)
    {
        if (row.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Name == "InlineDeliveryBox") is not { } box || box.Tag is "pd2") return;
        box.Tag = "pd2";
        box.AddHandler(TextBox.PastingFromClipboardEvent, OnDeliveryPasting, RoutingStrategies.Bubble);
    }

    /// <summary>Round 41 (PD-2): Ctrl+V in Delivery: a column from Excel fills the lines down; anything else pastes as usual.</summary>
    private async void OnDeliveryPasting(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanEditContent: true } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (e.Source is not TextBox box) return;
        e.Handled = true;
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception) { return; }
        if (string.IsNullOrEmpty(text)) return;
        PasteDelivery(vm, box, text);
    }

    /// <summary>What Ctrl+V in Delivery does with <paramref name="text"/> (also used by tests).</summary>
    public static void PasteDelivery(QuotationEditorViewModel vm, TextBox box, string text)
    {
        if (vm.PasteDeliveryColumn(text) > 0) return;
        PasteAsUsual(box, text);
    }

    /// <summary>What Ctrl+V in Rate / Qty / Disc % does with <paramref name="text"/> (also used by tests).</summary>
    public static void PasteIntoColumn(QuotationEditorViewModel vm, TextBox box, string text, PasteColumnKind kind)
    {
        if (vm.PasteColumn(text, kind) > 0) return;
        PasteAsUsual(box, text);
    }

    private static void PasteAsUsual(TextBox box, string text)
    {
        var line = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var current = box.Text ?? "";
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        box.Text = current[..start] + line + current[end..];
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = start + line.Length;
    }

    private async void OnRatePasting(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanEditContent: true } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (e.Source is not TextBox box) return;
        e.Handled = true; // a column of prices fills the lines; anything else is put in below, as a normal paste would
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception) { return; }
        if (string.IsNullOrEmpty(text)) return;
        PasteIntoRate(vm, box, text);
    }

    /// <summary>What Ctrl+V in Rate does with <paramref name="text"/>: a column of prices fills the lines down (also used by tests).</summary>
    public static void PasteIntoRate(QuotationEditorViewModel vm, TextBox box, string text)
    {
        if (vm.PastePriceColumn(text) > 0) return;
        var line = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var current = box.Text ?? "";
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        box.Text = current[..start] + line + current[end..];
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = start + line.Length;
    }

    /// <summary>What Ctrl+V in the Product box does with <paramref name="text"/> (also used by tests).</summary>
    public async Task PasteIntoProductAsync(QuotationEditorViewModel vm, TextBox box, string text)
    {
        if (Quotation.Core.Search.PastedListParser.LooksLikeAList(text) && await vm.OpenPastedListAsync(text))
        {
            // Enter adds the lines; Esc closes the box.
            FocusLater(PasteAddButton.IsEffectivelyEnabled ? PasteAddButton : PasteCancelButton);
            return;
        }
        // One line: typed into the box at the cursor, as a normal paste would.
        var line = string.Join(' ', text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var current = box.Text ?? "";
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        box.Text = current[..start] + line + current[end..];
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = start + line.Length;
    }

    /// <summary>"Search…" on a pasted line that was not found: a search box in its place, already searching the pasted words.</summary>
    private void OnPasteRowSearchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PasteRowViewModel row } button) return;
        Vm?.SearchPasteRowCommand.Execute(row);
        var picker = button.FindAncestorOfType<Grid>()?.GetVisualDescendants().OfType<SearchPicker>().FirstOrDefault();
        if (picker is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            picker.InputBox.Focus();
            picker.InputBox.SelectAll();
            picker.SearchNow();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>An item picked in the search box of a pasted line that was not found.</summary>
    private void OnPasteRowPicked(object? sender, ItemPickedEventArgs e)
    {
        if (sender is Control { DataContext: PasteRowViewModel row } && e.Item is ProductSearchHitDto hit) row.Pick(hit);
    }

    private void OnLostReasonLostFocus(object? sender, RoutedEventArgs e) => Vm?.SaveLostReasonCommand.Execute(null);

    /// <summary>The small calendar of "Ref. dated": opens below the box; picking a day fills it and closes.</summary>
    private void OnRefDateCalendarClick(object? sender, RoutedEventArgs e)
    {
        RefDatePopup.IsOpen = !RefDatePopup.IsOpen;
        if (RefDatePopup.IsOpen) RefDateCalendar.Focus();
    }

    private QuotationLineViewModel? _dragLine;

    /// <summary>The grip (⋮⋮) at the left of an item line: hold it and drag the line up or down.</summary>
    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: QuotationLineViewModel line } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragLine = line;
        line.IsDragging = true;
        // The list keeps the pointer while the rows are rebuilt in their new order.
        e.Pointer.Capture(LinesList);
        e.Handled = true;
    }

    private void OnLinesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragLine is null || Vm is not { } vm) return;
        var y = e.GetPosition(LinesList).Y;
        for (var i = 0; i < vm.Lines.Count; i++)
        {
            if (LinesList.ContainerFromIndex(i) is not Control row || row.TranslatePoint(new Point(0, 0), LinesList) is not { } top) continue;
            if (y < top.Y || y >= top.Y + row.Bounds.Height) continue;
            if (vm.Lines[i] != _dragLine && vm.MoveLine(_dragLine, i)) LinesList.UpdateLayout();
            break;
        }
    }

    private void EndDrag()
    {
        if (_dragLine is null) return;
        _dragLine.IsDragging = false;
        _dragLine = null;
    }

    private static void FocusLater(Control target) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => target.Focus(), Avalonia.Threading.DispatcherPriority.Input);
}
