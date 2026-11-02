using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Controls;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Renders the main screens in light and dark mode (and the search list highlight) when SCREENSHOT_DIR is set.</summary>
public class DarkModePreviewTests
{
    [AvaloniaFact]
    public async Task Capture_light_and_dark_screens()
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
        var customer = (await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;
        SaveQuotationRequest Req() => new()
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1, Description = "Brand: Mitutoyo" }],
        };
        var a = await session.Api.CreateQuotationAsync(Req());
        await session.Api.ApproveQuotationAsync(a.Id, overrideStaleData: true);
        await session.Api.CreateQuotationAsync(Req());
        await session.Api.ConvertToProformaAsync(a.Id);

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await session.Api.SetMyPreferencesAsync(new MyPreferencesDto(Theme: theme));
            session.Api.UpdateCurrentUser(await session.Api.MeAsync());
            var main = new MainWindowViewModel(session);
            var shell = new ShellViewModel(session, () => { });
            main.Content = shell;
            var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
            window.Show();

            async Task Shot(string name)
            {
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"{theme}-{name}.png"));
            }

            var dashboard = (DashboardViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => dashboard.Data is not null);
            await Shot("1-dashboard");

            shell.Navigate("new");
            var draft = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => draft.NumberText.Contains("provisional"));
            await draft.SelectCustomerAsync(customer.Id);
            draft.SelectProduct(product);
            draft.EntryQuantity = 2;
            draft.CommitEntryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var box = window.GetVisualDescendants().OfType<SearchPicker>().First(p => p.Name == "ProductBox");
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("micrometer");
            await UiHelpers.WaitUntil(() => box.IsDropDownOpen);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            await Shot("2-new-quotation-search-list");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            draft.IsDirty = false;
            draft.CancelEntryCommand.Execute(null);

            shell.OpenQuotation(a.Id);
            var editor = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => editor.Lines.Count > 0);
            await Shot("3-approved-quotation");

            shell.Navigate("history");
            var history = (QuotationListViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => history.Rows.Count >= 3);
            history.Selected = history.Rows[1];
            await Shot("4-history");

            shell.Navigate("mysettings");
            var mine = (MySettingsViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => !mine.IsBusy);
            await Shot("5-my-settings");

            shell.Dispose();
            window.Close();
        }
        ThemeService.Apply(AppTheme.Light);
    }
}
