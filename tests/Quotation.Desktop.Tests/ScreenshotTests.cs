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
        await session.Api.UploadSealAsync(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAJUlEQVR4nGM4ICDwn5aYYdSCUQtGLRi1YNSCUQtGLRi1YGhYAAC4lDX9H0TkSwAAAABJRU5ErkJggg=="));
        var settings = await session.Api.SettingsAsync();
        settings.SealPin = "1111";
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

        // Leaving unsaved work asks first.
        shell.Navigate("products");
        await UiHelpers.WaitUntil(() => shell.PendingPrompt is not null);
        await Shot("07-unsaved-prompt");
        shell.PendingPrompt!.Choose(LeaveChoice.Save);
        await UiHelpers.WaitUntil(() => shell.CurrentPage is ProductsViewModel);
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

        // Drafts → open a quotation (breadcrumbs and Back) → convert it to a proforma invoice.
        shell.Navigate("drafts");
        var drafts = (QuotationListViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => drafts.Rows.Count > 0);
        await Shot("08-drafts");
        drafts.OpenCommand.Execute(drafts.Rows.First(r => !r.IsProforma));
        var opened = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => opened.Lines.Count > 0);
        await Shot("09-opened-from-drafts");
        await opened.ConvertToProformaCommand.ExecuteAsync(null);
        var proforma = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => proforma.IsProforma && proforma.Lines.Count > 0);
        proforma.DeliveryNote = "DN/118";
        proforma.DispatchDocNo = "LR 55120";
        await Shot("10-proforma-invoice");

        // Office features: My Settings (Without GST, PDF folder), and the admin's Deleted list.
        shell.Navigate("mysettings");
        await UiHelpers.WaitUntil(() => shell.PendingPrompt is not null);
        shell.PendingPrompt!.Choose(LeaveChoice.DontSave);
        await UiHelpers.WaitUntil(() => shell.CurrentPage is MySettingsViewModel);
        await Task.Delay(300);
        await Shot("11-my-settings");
        shell.Navigate("deleted");
        await Task.Delay(300);
        await Shot("12-deleted");
        shell.Dispose();
    }
}
