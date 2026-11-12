using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 25: Test QR and payment note in Settings; the GST check before Approve.</summary>
public class Round25UiTests
{
    private sealed class Ctx : IDisposable
    {
        public required TestServer Server { get; init; }
        public required AppSession Session { get; init; }
        public required MainWindow Window { get; init; }
        public required ShellViewModel Shell { get; init; }
        public void Dispose()
        {
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    private static async Task<Ctx> SetupAsync(string user)
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        await server.LoginAsStandardAsync("ravi");
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync(user, user == "admin" ? "admin123" : user + "-pass");
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel && session.Settings is not null);
        return new Ctx { Server = server, Session = session, Window = window, Shell = shell };
    }

    private static async Task Shot(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public async Task Settings_test_qr_and_payment_note()
    {
        using var ctx = await SetupAsync("admin");
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is SettingsViewModel { Settings: not null });
        var settings = (SettingsViewModel)ctx.Shell.CurrentPage!;
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => t.Header as string == "Company");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        Assert.True(settings.UpiNoteBoth); // both numbers at first
        settings.ShowTestQrCommand.Execute(null);
        Assert.False(settings.IsTestQrOpen); // no UPI ID yet
        Assert.Contains("UPI ID", settings.ErrorMessage);

        settings.Settings!.Company.CompanyName = "T.SAIFUDDIN & CO.";
        view.FindControl<TextBox>("UpiIdBox")!.Text = "tsaifuddin@okhdfcbank"; // typed, not saved yet
        settings.ErrorMessage = null;
        view.FindControl<RadioButton>("UpiNoteOrderRadio")!.IsChecked = true;
        Assert.Equal(UpiNoteKind.OrderNumber, settings.Settings.Company.UpiNote);
        Assert.False(settings.UpiNoteBoth);
        view.FindControl<Button>("TestQrButton")!.BringIntoView();
        await Shot(ctx.Window, "r25-settings-upi");

        settings.ShowTestQrCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.IsTestQrOpen);
        Assert.NotNull(settings.TestQrImage);
        Assert.Equal("tsaifuddin@okhdfcbank", settings.TestQrUpiId);
        Assert.Equal("T.SAIFUDDIN & CO.", settings.TestQrPayee);
        Assert.True(view.FindControl<Border>("TestQrOverlay")!.IsVisible);
        await Shot(ctx.Window, "r25-test-qr");
        settings.CloseTestQrCommand.Execute(null);
        Assert.False(settings.IsTestQrOpen);

        await settings.SaveCommand.ExecuteAsync(null);
        Assert.Equal(UpiNoteKind.OrderNumber, (await ctx.Session.Api.SettingsAsync()).Company.UpiNote);
    }

    [AvaloniaFact]
    public async Task Gst_check_before_approve_go_back_or_approve_anyway()
    {
        using var ctx = await SetupAsync("ravi");
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var vm = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await vm.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        vm.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        vm.CommitEntryCommand.Execute(null);
        vm.FoldHeader();
        vm.BuyerState = "Karnataka";
        vm.BuyerStateCode = "29"; // the GSTIN 27… is from Maharashtra
        vm.RecalculateTotals();

        var approve = vm.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        Assert.Equal("Check the GST before approving", ctx.Shell.PendingDialog!.Title);
        Assert.Contains("from Maharashtra (27), but the state is Karnataka (29)", ctx.Shell.PendingDialog.Message);
        await Shot(ctx.Window, "r25-gst-check");
        ctx.Shell.AnswerDialogCommand.Execute(false); // Go back
        await approve;
        Assert.Equal(QuotationStatus.Draft, vm.Status);
        Assert.Null(vm.Id); // nothing saved either

        approve = vm.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        ctx.Shell.AnswerDialogCommand.Execute(true); // Approve anyway
        await approve;
        await UiHelpers.WaitUntil(() => vm.Status == QuotationStatus.Generated);

        // Everything matching: no question at all.
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e != vm && e.NumberText.Contains("provisional"));
        var next = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await next.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        next.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        next.CommitEntryCommand.Execute(null);
        await next.ApproveCommand.ExecuteAsync(null);
        Assert.Null(ctx.Shell.PendingDialog);
        Assert.Equal(QuotationStatus.Generated, next.Status);
    }
}
