using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Renders the folded and open buyer/references section when SCREENSHOT_DIR is set.</summary>
public class HeaderFoldPreviewTests
{
    [AvaloniaFact]
    public async Task Capture_folded_header()
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        using var server = new TestServer(products: 300, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var customer = (await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;

        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();

        async Task Shot(string name)
        {
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
        }

        shell.Navigate("new");
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync(customer.Id);
        editor.OtherReferences = "Your enquiry dated 02-Nov-2026";
        await Shot("1-before-enter");
        editor.FoldHeader();
        editor.SelectProduct(product);
        editor.EntryQuantity = 2;
        editor.CommitEntryCommand.Execute(null);
        await Shot("2-folded");
        editor.ToggleHeaderCommand.Execute(null);
        await Shot("3-shown-again");
        editor.FoldHeader();
        editor.Remarks = "Prices valid for 15 days";
        editor.ToggleFooterCommand.Execute(null);
        await Shot("4-footer-shown");
        editor.IsDirty = false;
        shell.Dispose();
        window.Close();
    }
}
