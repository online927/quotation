using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Back / Forward like a web browser, breadcrumbs, and the Save / Don't Save / Cancel question.</summary>
public class NavigationTests
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
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell } };
        window.Show();
        return new Ctx { Server = server, Session = session, Window = window, Shell = shell };
    }

    private static async Task<QuotationEditorViewModel> SavedQuotationAsync(Ctx ctx, string navKey = "new")
    {
        ctx.Shell.Navigate(navKey);
        var editor = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        Assert.True(await editor.SaveAsync());
        return editor;
    }

    [AvaloniaFact]
    public async Task Back_returns_to_the_list_as_it_was_left_and_forward_reopens_the_quotation()
    {
        using var ctx = await SetupAsync();
        var saved = await SavedQuotationAsync(ctx);

        ctx.Shell.Navigate("drafts");
        var drafts = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => drafts.Rows.Count == 1);
        drafts.Query = "sonepar";
        await UiHelpers.WaitUntil(() => !drafts.IsBusy && drafts.Rows.Count == 1);
        drafts.Selected = drafts.Rows[0];

        drafts.OpenCommand.Execute(drafts.Rows[0]);
        var opened = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => opened.Lines.Count == 1);
        Assert.True(ctx.Shell.CanGoBack);
        Assert.Equal(["Quotation " + saved.Number, "Draft Quotations", "Quotation " + saved.Number],
            ctx.Shell.Breadcrumbs.Select(c => c.Title).TakeLast(3));
        Assert.Contains("Draft Quotations", ctx.Shell.BackToolTip);

        // The ‹ button: the very same list page, with its search text and selection.
        ctx.Shell.GoBack();
        Assert.Same(drafts, ctx.Shell.CurrentPage);
        Assert.Equal("sonepar", drafts.Query);
        await UiHelpers.WaitUntil(() => !drafts.IsBusy && drafts.Selected is not null);
        Assert.Equal(saved.Id, drafts.Selected!.Id);
        Assert.True(ctx.Shell.NavItems.Single(n => n.Key == "drafts").IsSelected);

        Assert.True(ctx.Shell.CanGoForward);
        ctx.Shell.GoForward();
        Assert.Same(opened, ctx.Shell.CurrentPage);

        // A breadcrumb jumps several pages back.
        ctx.Shell.GoToCrumbCommand.Execute(ctx.Shell.Breadcrumbs.Single(c => c.StepsBack == 2));
        Assert.Same(saved, ctx.Shell.CurrentPage);
    }

    [AvaloniaFact]
    public async Task Leaving_unsaved_work_asks_save_dont_save_or_cancel()
    {
        using var ctx = await SetupAsync();
        ctx.Shell.Navigate("new");
        var editor = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        Assert.True(editor.HasUnsavedChanges);

        // Cancel: stay on the page, nothing lost.
        ctx.Shell.GoBack();
        Assert.NotNull(ctx.Shell.PendingPrompt);
        Assert.Contains("New Quotation", ctx.Shell.PendingPrompt!.Message);
        ctx.Shell.AnswerPromptCommand.Execute(LeaveChoice.Cancel);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingPrompt is null);
        Assert.Same(editor, ctx.Shell.CurrentPage);
        Assert.Single(editor.Lines);

        // Save: saved on the server, then the dashboard.
        ctx.Shell.Navigate("dashboard");
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingPrompt is not null);
        ctx.Shell.AnswerPromptCommand.Execute(LeaveChoice.Save);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is DashboardViewModel);
        Assert.NotNull(editor.Id);
        Assert.False(editor.IsDirty);

        // Don't Save: changes thrown away (the saved version stays as it was).
        ctx.Shell.GoBack();
        Assert.Same(editor, ctx.Shell.CurrentPage);
        editor.Remarks = "changed but not saved";
        ctx.Shell.GoBack();
        ctx.Shell.AnswerPromptCommand.Execute(LeaveChoice.DontSave);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is DashboardViewModel);
        Assert.Equal("", (await ctx.Session.Api.QuotationAsync(editor.Id!.Value)).Remarks);
    }

    [AvaloniaFact]
    public async Task Esc_alt_left_and_the_prompt_keys_work_from_the_keyboard()
    {
        using var ctx = await SetupAsync();
        ctx.Shell.Navigate("drafts");
        ctx.Shell.Navigate("history");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);

        ctx.Window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, null);
        Assert.Equal("Draft Quotations", ((QuotationListViewModel)ctx.Shell.CurrentPage!).Title);
        ctx.Window.KeyPress(Key.Right, RawInputModifiers.Alt, PhysicalKey.ArrowRight, null);
        Assert.Equal("Quotation History", ((QuotationListViewModel)ctx.Shell.CurrentPage!).Title);

        ctx.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal("Draft Quotations", ((QuotationListViewModel)ctx.Shell.CurrentPage!).Title);

        // In the editor, Esc first cancels a line being edited; with nothing to cancel it goes back (asking first).
        ctx.Shell.Navigate("new");
        var editor = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.Remarks = "typed";
        ctx.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingPrompt is not null);
        ctx.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);   // Esc = Cancel
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingPrompt is null);
        Assert.Same(editor, ctx.Shell.CurrentPage);
        Assert.Equal("typed", editor.Remarks);

        ctx.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingPrompt is not null);
        ctx.Window.KeyPress(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");                // N = Don't Save
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationListViewModel);
    }

    [AvaloniaFact]
    public async Task Proforma_invoice_from_the_menu_and_from_a_quotation()
    {
        using var ctx = await SetupAsync();
        var proforma = await SavedQuotationAsync(ctx, "newproforma");
        Assert.True(proforma.IsProforma);
        Assert.StartsWith("TSPI", proforma.Number);
        Assert.Equal("Invoice No.", proforma.NumberLabel);
        Assert.False(proforma.CanConvertToProforma);

        var quotation = await SavedQuotationAsync(ctx);
        Assert.StartsWith("TSQ", quotation.Number);
        Assert.True(quotation.CanConvertToProforma);
        await quotation.ConvertToProformaCommand.ExecuteAsync(null);
        var converted = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => converted.IsProforma && converted.Lines.Count == 1);
        Assert.Equal("Proforma Invoice " + converted.Number, converted.Title);
        Assert.Contains(quotation.Number!, converted.OtherReferences);

        // Back goes to the quotation it was made from.
        ctx.Shell.GoBack();
        Assert.Same(quotation, ctx.Shell.CurrentPage);

        ctx.Shell.Navigate("proforma");
        var list = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 2);
        Assert.All(list.Rows, r => Assert.True(r.IsProforma));
        list.TypeFilter = QuotationListViewModel.TypeFilters[0];
        await UiHelpers.WaitUntil(() => list.Rows.Count == 3);
    }
}
