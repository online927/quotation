using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Renders the editor's grouped action bar (draft and approved, wide and narrow) when SCREENSHOT_DIR is set.</summary>
public class ButtonBarPreviewTests
{
    [AvaloniaFact]
    public async Task Capture_button_bar()
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

        var customer = (await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;
        var approved = await session.Api.CreateQuotationAsync(new Contracts.SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = [new Contracts.QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1 }],
        });
        await session.Api.ApproveQuotationAsync(approved.Id, overrideStaleData: true);

        foreach (var width in new[] { 1920, 1366 })
        {
            var main = new MainWindowViewModel(session);
            var shell = new ShellViewModel(session, () => { });
            main.Content = shell;
            var window = new MainWindow { DataContext = main, Width = width, Height = width > 1500 ? 1040 : 740 };
            window.Show();

            async Task Shot(string name)
            {
                await Task.Delay(300);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"{width}-{name}.png"));
            }

            var dashboard = (DashboardViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => dashboard.Data is not null);

            shell.Navigate("new");
            var draft = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => draft.NumberText.Contains("provisional"));
            await draft.SelectCustomerAsync(customer.Id);
            draft.SelectProduct(product);
            draft.EntryQuantity = 2;
            draft.CommitEntryCommand.Execute(null);
            await Shot("1-new-quotation");
            draft.IsDirty = false;
            draft.CancelEntryCommand.Execute(null);

            shell.OpenQuotation(approved.Id);
            var editor = (QuotationEditorViewModel)shell.CurrentPage!;
            await UiHelpers.WaitUntil(() => editor.Lines.Count > 0 && editor.CanSeal);
            await Shot("2-approved-quotation");

            shell.Dispose();
            window.Close();
        }
    }
}
