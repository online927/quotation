using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;
using Quotation.Server.Services;

namespace Quotation.Desktop.Tests;

/// <summary>Renders the quotation screen (fewer buttons, folded header, speed check) and Diagnostics → Slow requests.</summary>
public class SpeedPreviewTests
{
    [AvaloniaFact]
    public async Task Capture_speed_check_and_slow_requests()
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
        var q = await session.Api.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id, OtherReferences = "Your enquiry dated 02-Nov-2026",
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1 }],
        });
        await session.Api.ApproveQuotationAsync(q.Id, overrideStaleData: true);
        var log = server.Services.GetRequiredService<SlowRequestLog>();
        var now = DateTime.UtcNow;
        log.ServerSlow("a1", now.AddMinutes(-40), "keerthana", "USER-PC", "Search products", 1900, "Tally sync");
        log.ClientReport(new SlowRequestReport("a1", "Search products", 2100, 1900, "Tally sync"), "keerthana", "USER-PC", now.AddMinutes(-40));
        log.ClientReport(new SlowRequestReport("b2", "Customer details", 3200, 40, null), "rasik", "RASIK-PC", now.AddMinutes(-12));
        log.ClientReport(new SlowRequestReport("c3", "Open quotation", 5000, null, null), "rasik", "RASIK-PC", now.AddMinutes(-5));

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

        await UiHelpers.WaitUntil(() => shell.SpeedText.Contains("ms"));
        shell.OpenQuotation(q.Id);
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => editor.Lines.Count > 0);
        await Shot("1-quotation");

        shell.Navigate("diagnostics");
        var diag = (DiagnosticsViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => diag.SlowRequests.Count >= 3);
        Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string?)t.Header == "Slow requests");
        await Shot("2-slow-requests");
        shell.Dispose();
        window.Close();
    }
}
