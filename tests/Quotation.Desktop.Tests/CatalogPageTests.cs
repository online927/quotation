using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class CatalogPageTests
{
    private static async Task<(TestServer, AppSession, MainWindow)> SetupAsync()
    {
        var server = new TestServer(products: 300, customers: 100, today: new DateOnly(2026, 9, 30));
        var session = new AppSession(new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") }, _ => server.CreateApi());
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) };
        window.Show();
        return (server, session, window);
    }

    [AvaloniaFact]
    public async Task Product_search_as_you_type_shows_detail()
    {
        var (server, session, window) = await SetupAsync();
        using var _ = server;
        var vm = new ProductsViewModel(session);
        ((MainWindowViewModel)window.DataContext!).Content = vm;
        await UiHelpers.WaitUntil(() => vm.Results.Count > 0);

        vm.Query = "universal bevel";
        await UiHelpers.WaitUntil(() => vm.Results.Count > 0 && vm.Results[0].Product.Name.Contains("UNIVERSAL"));
        vm.Selected = vm.Results[0];
        await UiHelpers.WaitUntil(() => vm.Detail is not null);
        Assert.Equal("90172020", vm.Detail!.Hsn);
        Assert.NotNull(window.FindDescendantOfType<ProductsView>());
    }

    [AvaloniaFact]
    public async Task Customer_search_shows_address_and_ship_to()
    {
        var (server, session, window) = await SetupAsync();
        using var _ = server;
        var vm = new CustomersViewModel(session);
        ((MainWindowViewModel)window.DataContext!).Content = vm;

        vm.Query = "SONEPAR";
        await UiHelpers.WaitUntil(() => vm.Results.Count == 2);
        vm.Selected = vm.Results.First(r => r.Customer.Name == "SONEPAR INDIA PRIVATE LIMITED");
        await UiHelpers.WaitUntil(() => vm.Detail?.Name == "SONEPAR INDIA PRIVATE LIMITED");
        Assert.Contains("Chakan, Pune", vm.AddressText);
        Assert.Contains("Bangalore Warehouse", vm.ShipToText);
        Assert.NotNull(window.FindDescendantOfType<CustomersView>());
    }
}
