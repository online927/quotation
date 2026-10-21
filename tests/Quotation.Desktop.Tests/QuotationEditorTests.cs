using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Controls;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

internal sealed class RecordingLauncher(ClientConfig config) : DocumentLauncher(config)
{
    public List<string> Saved { get; } = [];

    public override string SaveAndOpen(byte[] pdf, string fileName, bool open = true)
    {
        Saved.Add(fileName);
        return fileName;
    }
}

public class QuotationEditorTests
{
    private sealed class Ctx : IDisposable
    {
        public required TestServer Server;
        public required AppSession Session;
        public required MainWindow Window;
        public required ShellViewModel Shell;
        public void Dispose()
        {
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    private static async Task<Ctx> SetupAsync()
    {
        var server = new TestServer(products: 300, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi());
        session.Documents = new RecordingLauncher(config);
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main };
        window.Show();
        return new Ctx { Server = server, Session = session, Window = window, Shell = shell };
    }

    private static async Task<QuotationEditorViewModel> NewEditor(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        var editor = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        return editor;
    }

    [AvaloniaFact]
    public async Task Create_save_and_approve_a_quotation()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        Assert.NotNull(ctx.Window.FindDescendantOfType<QuotationEditorView>());

        var customer = (await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First();
        await editor.SelectCustomerAsync(customer.Customer.Id);
        Assert.Equal("Sonepar India Private Limited", editor.BuyerName);
        Assert.Equal("27AAACS1234F1Z3", editor.BuyerGstin);
        Assert.Equal(3, editor.ShipToOptions.Count); // same as buyer, Bangalore warehouse, other

        var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
        editor.SelectProduct(product);
        Assert.Equal(15600m, editor.EntryRate);
        editor.EntryQuantity = 2;
        editor.CommitEntryCommand.Execute(null);
        Assert.Single(editor.Lines);
        Assert.Equal("₹ 31,200.00", editor.GrandTotalText);
        Assert.Equal("INR Thirty One Thousand Two Hundred Only", editor.AmountInWords);
        Assert.Equal("2 NOS", editor.TotalQuantityText);

        Assert.True(await editor.SaveAsync());
        Assert.NotNull(editor.Number);
        await editor.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("Preview-" + editor.Number + ".pdf", ((RecordingLauncher)ctx.Session.Documents).Saved);
        Assert.False(editor.IsDirty);
        Assert.Equal(QuotationStatus.Draft, editor.Status);

        await editor.ApproveCommand.ExecuteAsync(null);
        Assert.Equal(QuotationStatus.Generated, editor.Status);
        Assert.True(editor.HasPdf);
        var launcher = (RecordingLauncher)ctx.Session.Documents;
        Assert.Contains(editor.Number + ".pdf", launcher.Saved);
    }

    [AvaloniaFact]
    public async Task Keyboard_line_entry_adds_lines_with_enter()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var product = (await ctx.Session.Api.SearchProductsAsync("530-104")).First().Product;
        editor.SelectProduct(product);

        var qty = view.FindControl<NumericUpDown>("QtyBox")!;
        qty.Focus();
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        ctx.Window.KeyTextInput("3");
        ctx.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);   // → rate
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);   // → discount
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyTextInput("10");
        ctx.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);   // add line
        await UiHelpers.WaitUntil(() => editor.Lines.Count == 1);

        var line = editor.Lines[0];
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(3250m, line.Rate);
        Assert.Equal(10m, line.DiscountPercent);
        Assert.Equal(8775m, line.Amount);
        Assert.Null(editor.EntryProduct); // entry cleared for the next product
    }

    private static void Press(Ctx ctx, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        ctx.Window.KeyPress(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<SearchPicker> TypeInto(Ctx ctx, string pickerName, string text)
    {
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var box = view.FindControl<SearchPicker>(pickerName)!;
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.InputBox.IsKeyboardFocusWithin);
        ctx.Window.KeyTextInput(text);
        await UiHelpers.WaitUntil(() => box.IsDropDownOpen);
        return box;
    }

    [AvaloniaFact]
    public async Task Product_list_is_used_like_tally_and_the_line_is_added()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var box = await TypeInto(ctx, "ProductBox", "carbide end mill");
        var hits = box.Results.Cast<ProductSearchHitDto>().ToList();
        Assert.True(hits.Count >= 2);
        Assert.Equal(0, box.HighlightedIndex); // first match highlighted, nothing picked yet
        Assert.Null(editor.EntryProduct);

        // Arrow keys only move the highlight: the typed text, the focus and the entry stay as they are.
        Press(ctx, Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(1, box.HighlightedIndex);
        Press(ctx, Key.Down, PhysicalKey.ArrowDown);
        Press(ctx, Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(1, box.HighlightedIndex);
        Assert.Equal("carbide end mill", box.InputBox.Text);
        Assert.True(box.IsDropDownOpen);
        Assert.True(box.InputBox.IsKeyboardFocusWithin);
        Assert.Null(editor.EntryProduct);

        // Enter accepts the highlighted product and moves to Quantity.
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        var picked = hits[1].Product;
        await UiHelpers.WaitUntil(() => editor.EntryProduct?.Id == picked.Id);
        Assert.Equal(picked.Name, box.InputBox.Text);
        Assert.False(box.IsDropDownOpen);
        var qty = view.FindControl<NumericUpDown>("QtyBox")!;
        await UiHelpers.WaitUntil(() => qty.IsKeyboardFocusWithin);

        // Quantity → Rate → Disc % → Enter adds the line.
        Press(ctx, Key.A, PhysicalKey.A, RawInputModifiers.Control);
        ctx.Window.KeyTextInput("2");
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        Press(ctx, Key.A, PhysicalKey.A, RawInputModifiers.Control);
        ctx.Window.KeyTextInput("1450");
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        await UiHelpers.WaitUntil(() => editor.Lines.Count == 1);
        Assert.Null(editor.ErrorMessage);
        var line = editor.Lines[0];
        Assert.Equal(picked.Name, line.ItemName);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(1450m, line.Rate);

        // The entry is cleared for the next product and the old list does not come back.
        await UiHelpers.WaitUntil(() => box.InputBox.IsKeyboardFocusWithin);
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", box.InputBox.Text);
        Assert.Equal("", editor.ProductSearchText);
        Assert.False(box.IsDropDownOpen);
        Assert.Null(editor.EntryProduct);
    }

    [AvaloniaFact]
    public async Task Reported_item_without_a_tally_rate_is_added_after_typing_its_rate()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var box = await TypeInto(ctx, "ProductBox", "112-201");
        var top = Assert.IsType<ProductSearchHitDto>(box.Results[0]);
        Assert.Equal("112-201-POINT MICROMETER", top.Product.Name);
        Assert.Equal("", top.Product.Brand);
        Assert.Null(top.Product.Rate);

        Press(ctx, Key.Enter, PhysicalKey.Enter);
        await UiHelpers.WaitUntil(() => editor.EntryProduct is not null);
        Assert.Equal("112-201-POINT MICROMETER", box.InputBox.Text);
        Assert.Null(editor.EntryRate); // no rate in Tally: the user types it

        await UiHelpers.WaitUntil(() => view.FindControl<NumericUpDown>("QtyBox")!.IsKeyboardFocusWithin);
        Press(ctx, Key.Enter, PhysicalKey.Enter);          // quantity 1 → rate
        ctx.Window.KeyTextInput("2350");
        Press(ctx, Key.Enter, PhysicalKey.Enter);          // → disc %
        Press(ctx, Key.Enter, PhysicalKey.Enter);          // add line
        await UiHelpers.WaitUntil(() => editor.Lines.Count == 1);
        Assert.Equal("112-201-POINT MICROMETER", editor.Lines[0].ItemName);
        Assert.Equal(2350m, editor.Lines[0].Rate);
        Assert.Equal("90173010", editor.Lines[0].Hsn);
    }

    [AvaloniaFact]
    public async Task Product_can_be_picked_with_the_mouse()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var box = await TypeInto(ctx, "ProductBox", "carbide end mill");
        var hits = box.Results.Cast<ProductSearchHitDto>().ToList();

        ListBoxItem? item = null;
        await UiHelpers.WaitUntil(() => (item = box.ResultList.ContainerFromIndex(1) as ListBoxItem) is { IsArrangeValid: true });
        var top = TopLevel.GetTopLevel(item)!;
        var point = item!.TranslatePoint(new Point(20, 6), top)!.Value;
        top.MouseDown(point, MouseButton.Left);
        top.MouseUp(point, MouseButton.Left);

        await UiHelpers.WaitUntil(() => editor.EntryProduct?.Id == hits[1].Product.Id);
        Assert.Equal(hits[1].Product.Name, box.InputBox.Text);
        Assert.False(box.IsDropDownOpen);
    }

    [AvaloniaFact]
    public async Task Enter_pressed_while_typing_takes_the_top_match_for_the_full_text()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var box = view.FindControl<SearchPicker>("ProductBox")!;
        var expected = (await ctx.Session.Api.SearchProductsAsync("530-104")).First().Product;
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        ctx.Window.KeyTextInput("530-104");
        Press(ctx, Key.Enter, PhysicalKey.Enter); // before the list has appeared

        await UiHelpers.WaitUntil(() => editor.EntryProduct is not null);
        Assert.Equal(expected.Id, editor.EntryProduct!.Id);
        Assert.Equal(expected.Name, box.InputBox.Text);
    }

    [AvaloniaFact]
    public async Task Typing_over_a_picked_product_unpicks_it_and_esc_closes_the_list()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var box = await TypeInto(ctx, "ProductBox", "530-104");
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        await UiHelpers.WaitUntil(() => editor.EntryProduct is not null);

        // Change the text: the product is no longer picked, so it cannot be added by mistake.
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyTextInput(" X");
        Assert.Null(editor.EntryProduct);
        await UiHelpers.WaitUntil(() => box.IsDropDownOpen);
        Press(ctx, Key.Escape, PhysicalKey.Escape);
        Assert.False(box.IsDropDownOpen);
        Assert.EndsWith(" X", box.InputBox.Text);

        // Enter on un-picked text shows the matches again instead of moving on.
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        await UiHelpers.WaitUntil(() => box.IsDropDownOpen || editor.EntryProduct is not null || box.Results.Count == 0);
        Assert.Null(editor.EntryProduct);
        Assert.True(box.InputBox.IsKeyboardFocusWithin);

        editor.CommitEntryCommand.Execute(null);
        Assert.Contains("Select a product", editor.ErrorMessage);
        Assert.Empty(editor.Lines);
    }

    [AvaloniaFact]
    public async Task Customer_picked_from_the_list_fills_the_buyer()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var box = await TypeInto(ctx, "CustomerBox", "SONEPAR INDIA");
        Assert.Equal(0, box.HighlightedIndex);
        Press(ctx, Key.Enter, PhysicalKey.Enter);
        await UiHelpers.WaitUntil(() => editor.BuyerName == "Sonepar India Private Limited");

        Assert.Equal("SONEPAR INDIA PRIVATE LIMITED", box.InputBox.Text); // Tally ledger name
        Assert.False(box.IsDropDownOpen);
        var product = view.FindControl<SearchPicker>("ProductBox")!;
        await UiHelpers.WaitUntil(() => product.InputBox.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task Rate_override_is_marked_and_lines_can_be_edited_and_removed()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var bevel = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
        editor.SelectProduct(bevel);
        editor.EntryRate = 15000;
        editor.CommitEntryCommand.Execute(null);
        Assert.True(editor.Lines[0].RateOverridden);
        Assert.Contains("15,600.00", editor.Lines[0].TallyRateHint);

        editor.EditLineCommand.Execute(editor.Lines[0]);
        Assert.Equal("Update line", editor.EntryButtonText);
        editor.EntryQuantity = 4;
        editor.CommitEntryCommand.Execute(null);
        Assert.Single(editor.Lines);
        Assert.Equal(60000m, editor.Lines[0].Amount);

        editor.RemoveLineCommand.Execute(editor.Lines[0]);
        Assert.Empty(editor.Lines);
        Assert.False(editor.CanApprove);
    }

    [AvaloniaFact]
    public async Task Validation_problems_are_listed()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        var noRate = (await ctx.Session.Api.SearchProductsAsync("carbide end mill 10mm 4 flute")).First().Product;
        editor.SelectProduct(noRate);
        editor.CommitEntryCommand.Execute(null);
        await editor.ApproveCommand.ExecuteAsync(null);
        Assert.Contains(editor.ValidationMessages, m => m.Contains("Select a customer"));
        Assert.Contains(editor.ValidationMessages, m => m.Contains("Rate must be greater than zero"));
        Assert.Equal(QuotationStatus.Draft, editor.Status);
    }

    [AvaloniaFact]
    public async Task Stale_data_prompts_for_override()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("BHARAT PRECISION")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        await editor.SaveAsync();

        ctx.Server.Tally.Online = false;
        await ctx.Session.Api.TestTallyAsync();
        await editor.ApproveCommand.ExecuteAsync(null);
        Assert.True(editor.NeedsStaleOverride);
        Assert.Equal(QuotationStatus.Draft, editor.Status);

        await editor.ApproveAnywayCommand.ExecuteAsync(null);
        Assert.Equal(QuotationStatus.Generated, editor.Status);
        Assert.NotNull(editor.DataWarning);
    }

    [AvaloniaFact]
    public async Task Drafts_list_opens_and_duplicates_quotations()
    {
        using var ctx = await SetupAsync();
        var editor = await NewEditor(ctx);
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        await editor.SaveAsync();
        var number = editor.Number;

        ctx.Shell.Navigate("drafts");
        var list = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 1);
        Assert.Equal(number, list.Rows[0].Number);
        Assert.NotNull(ctx.Window.FindDescendantOfType<QuotationListView>());

        list.OpenCommand.Execute(list.Rows[0]);
        var reopened = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => reopened.Number == number);
        Assert.Single(reopened.Lines);

        ctx.Shell.Navigate("history");
        var history = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => history.Rows.Count == 1);
        await history.DuplicateCommand.ExecuteAsync(history.Rows[0]);
        var copy = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => copy.Number is not null && copy.Number != number);
    }
}
