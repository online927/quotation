using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>
/// Renders key screens to PNG when SCREENSHOT_DIR is set (used to review layouts in CI artifacts).
/// </summary>
public class ScreenshotTests
{
    [AvaloniaFact]
    public async Task Capture_main_screens()
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        using var server = new TestServer(products: 400, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var settings = await session.Api.SettingsAsync();
        settings.Company.CompanyName = "T.SAIFUDDIN & CO.";
        settings.Company.StateCode = "29";
        await session.Api.SaveSettingsAsync(settings);

        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1440, Height = 900 };
        window.Show();

        async Task Shot(string name)
        {
            await UiHelpers.WaitUntil(() => true);
            await Task.Delay(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
        }

        await Task.Delay(500);
        await Shot("01-dashboard");

        shell.Navigate("new");
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.EntryQuantity = 2;
        editor.EntryDescription = "Brand: Mitutoyo\nModel: 187-901\nMOQ: 1 No.";
        editor.CommitEntryCommand.Execute(null);
        editor.SelectProduct((await session.Api.SearchProductsAsync("3 core 2.5 sqmm polycab fr")).First().Product);
        editor.EntryQuantity = 100;
        editor.EntryRate = 120;
        editor.CommitEntryCommand.Execute(null);
        editor.BuyersReference = "PO/4471";
        await Shot("02-quotation-editor");

        shell.Navigate("products");
        var products = (ProductsViewModel)shell.CurrentPage!;
        products.Query = "3 core 2.5 sqmm cable";
        await UiHelpers.WaitUntil(() => products.Results.Count > 0 && products.ResultInfo.Contains("match"));
        await UiHelpers.WaitUntil(() => products.Detail is not null);
        await Shot("03-products");

        server.AiProvider = new ScriptedProvider(async ai =>
        {
            var p = await ai.Call("search_product", new { query = "3 core 2.5 sqmm cable" });
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Sonepar", null,
                ScriptedProvider.Line("3 core 2.5 sqmm cable", "3 core 2.5 sqmm cable", null, 10)));
        });
        shell.Navigate("inbox");
        var inbox = (AiInboxViewModel)shell.CurrentPage!;
        inbox.RequestText = "Please quote 10 pcs 3 core 2.5 sqmm cable for Sonepar";
        await inbox.AnalyzeCommand.ExecuteAsync(null);
        await Shot("06-ai-inbox");

        shell.Navigate("tally");
        await Task.Delay(500);
        await Shot("04-tally");
        shell.Navigate("settings");
        await Task.Delay(500);
        await Shot("05-settings");
        shell.Dispose();
    }
}
