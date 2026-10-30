using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Renders the colour scheme with sidebar A (dark navy) and B (light blue) when SCREENSHOT_DIR is set.</summary>
public class ColourPreviewTests
{
    private static readonly Dictionary<string, string> LightSidebar = new()
    {
        ["SidebarBrush"] = "#EEF4FD", ["SidebarTextBrush"] = "#1D1D1F", ["SidebarMutedBrush"] = "#6E7B8C", ["SidebarHoverBrush"] = "#DCE8FA",
        ["SidebarSelectedBrush"] = "#0071E3", ["SidebarSelectedTextBrush"] = "#FFFFFF", ["SidebarBorderBrush"] = "#DCE6F3",
    };

    [AvaloniaFact]
    public async Task Capture_colour_options()
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
        settings.Company.CompanyName = "T.SAIFUDDIN & CO.";
        settings.Company.StateCode = "29";
        await session.Api.SaveSettingsAsync(settings);

        // Some documents in different states for the lists.
        var customer = (await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;
        Contracts.SaveQuotationRequest Req() => new()
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = [new Contracts.QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1 }],
        };
        var a = await session.Api.CreateQuotationAsync(Req());
        await session.Api.ApproveQuotationAsync(a.Id, overrideStaleData: true);
        var b = await session.Api.CreateQuotationAsync(Req());
        await session.Api.CancelQuotationAsync(b.Id);
        await session.Api.CreateQuotationAsync(Req());
        var p = await session.Api.ConvertToProformaAsync(a.Id);

        foreach (var (variant, brushes) in new[] { ("A-dark-sidebar", (Dictionary<string, string>?)null), ("B-light-sidebar", LightSidebar) })
        {
            var originals = new Dictionary<string, object?>();
            if (brushes is not null)
            {
                foreach (var (key, colour) in brushes)
                {
                    originals[key] = Application.Current!.Resources[key];
                    Application.Current.Resources[key] = new SolidColorBrush(Color.Parse(colour));
                }
            }

            var main = new MainWindowViewModel(session);
            var shell = new ShellViewModel(session, () => { });
            main.Content = shell;
            var window = new MainWindow { DataContext = main, Width = 1440, Height = 900 };
            window.Show();

            async Task Shot(string name)
            {
                await Task.Delay(300);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"{variant}-{name}.png"));
            }

            var dashboard = (DashboardViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => dashboard.Data is not null);
            await Shot("1-dashboard");

            shell.OpenQuotation(a.Id);
            var editor = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => editor.Lines.Count > 0 && editor.CanSeal);
            await Shot("2-quotation-approved");

            shell.Navigate("new");
            var draft = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => draft.NumberText.Contains("provisional"));
            await draft.SelectCustomerAsync(customer.Id);
            draft.SelectProduct(product);
            draft.EntryQuantity = 2;
            draft.CommitEntryCommand.Execute(null);
            await Shot("3-new-quotation");
            draft.IsDirty = false;
            draft.CancelEntryCommand.Execute(null);

            shell.Navigate("history");
            var history = (QuotationListViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => history.Rows.Count >= 4);
            await Shot("4-history");

            shell.OpenQuotation(p.Id);
            var proforma = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => proforma.Lines.Count > 0);
            await Shot("5-proforma");

            shell.Dispose();
            window.Close();
            foreach (var (key, value) in originals) Application.Current!.Resources[key] = value;
        }
    }
}
