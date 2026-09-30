using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Quotation.Core.Domain;
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
        Assert.False(editor.IsDirty);
        Assert.Equal(QuotationStatus.Draft, editor.Status);

        await editor.ApproveCommand.ExecuteAsync(null);
        Assert.Equal(QuotationStatus.Approved, editor.Status);
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
        Assert.Equal(QuotationStatus.Approved, editor.Status);
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
