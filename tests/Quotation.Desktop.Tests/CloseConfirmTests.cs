using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Closing the window asks first: "Close TS Quotation?", or Save / Don't Save / Cancel with unsaved work.</summary>
public class CloseConfirmTests
{
    [AvaloniaFact]
    public async Task Closing_the_app_asks_first()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        async Task Shot(string name)
        {
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
        }
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);

        // "Bro, are you trying to close?" - No keeps the app open.
        var asking = shell.ConfirmCloseAsync();
        await UiHelpers.WaitUntil(() => shell.PendingDialog is not null);
        Assert.Equal("Bro, are you trying to close?", shell.PendingDialog!.Title);
        Assert.Equal("Yes", shell.PendingDialog.PrimaryText);
        Assert.Equal("No", shell.PendingDialog.SecondaryText);
        await Shot("c1-close-question");
        shell.AnswerDialogCommand.Execute(false);
        Assert.False(await asking);

        // A quotation with unsaved changes: the same question; "Yes, close" keeps the work on this PC for next time.
        shell.Navigate("new");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        asking = shell.ConfirmCloseAsync();
        await UiHelpers.WaitUntil(() => shell.PendingDialog is not null);
        Assert.Equal("Bro, are you trying to close?", shell.PendingDialog!.Title);
        shell.AnswerDialogCommand.Execute(true);
        Assert.True(await asking);
        Assert.Null(editor.Number); // not saved to the server
        Assert.True(File.Exists(Path.Combine(server.DataDirectory, "recovery", "admin.json"))); // kept for next time

        shell.Dispose();
        window.Close();
    }
}
