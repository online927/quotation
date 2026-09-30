using Avalonia.Headless.XUnit;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class TallyPageTests
{
    [AvaloniaFact]
    public async Task Test_connection_shows_company_and_year()
    {
        using var server = new TestServer(today: new DateOnly(2026, 9, 30));
        var session = new AppSession(new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") }, _ => server.CreateApi());
        await session.Api.LoginAsync("admin", "admin123");
        var vm = new TallyViewModel(session);
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = vm } };
        window.Show();

        await UiHelpers.WaitUntil(() => vm.Status is not null);
        Assert.Equal("DISCONNECTED", vm.ConnectionText);

        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.TestResult!.Success);
        Assert.Equal("CONNECTED", vm.ConnectionText);
        Assert.Equal("2026-27", vm.Status!.ActiveFinancialYear);
        Assert.NotNull(window.FindDescendantOfType<TallyView>());

        await vm.IncrementalSyncCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.NotEmpty(vm.Runs);
        Assert.Equal("Succeeded", vm.Runs[0].Status);
        Assert.Equal("Full", vm.Runs[0].Kind); // first sync is always full
        Assert.True(vm.Status!.ProductCount > 0);

        vm.SampleName = "187-901-10-UNIVERSAL BEVEL PROTRACTOR";
        await vm.LoadSampleCommand.ExecuteAsync(null);
        Assert.Contains("90172020", vm.SampleXml);

        server.Tally.Online = false;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.False(vm.TestResult!.Success);
        Assert.Equal("DISCONNECTED", vm.ConnectionText);
    }
}
