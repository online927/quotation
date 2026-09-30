using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class HistoryUiTests
{
    [AvaloniaFact]
    public async Task Global_search_recent_items_audit_and_diagnostics()
    {
        using var server = new TestServer(products: 300, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell } };
        window.Show();

        // First quotation.
        shell.Navigate("new");
        var e1 = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => e1.NumberText.Contains("provisional"));
        await e1.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        e1.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
        e1.CommitEntryCommand.Execute(null);
        await e1.SaveAsync();
        await e1.LoadAuditTrailCommand.ExecuteAsync(null);
        Assert.Contains(e1.AuditTrail, a => a.Contains("QuotationCreated"));

        // Second quotation offers the recent customer and product.
        shell.Navigate("new");
        var e2 = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => e2.HasRecentCustomers && e2.HasRecentProducts);
        await e2.PickRecentCustomerCommand.ExecuteAsync(e2.RecentCustomers[0]);
        Assert.Equal("27AAACS1234F1Z3", e2.BuyerGstin);
        await e2.PickRecentProductCommand.ExecuteAsync(e2.RecentProducts[0]);
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", e2.EntryProduct!.Name);

        // Global search.
        shell.GlobalSearch = e1.Number!;
        shell.SearchCommand.Execute(null);
        var list = Assert.IsType<QuotationListViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 1);
        Assert.Equal(e1.Number, list.Rows[0].Number);

        shell.Navigate("diagnostics");
        var diag = Assert.IsType<DiagnosticsViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => diag.Info is not null);
        Assert.NotEmpty(diag.Audit);
        Assert.NotNull(window.FindDescendantOfType<DiagnosticsView>());
        shell.Dispose();
    }
}
