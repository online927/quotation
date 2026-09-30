using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class ShellTests
{
    private static (MainWindow window, MainWindowViewModel vm, AppSession session) Open(TestServer server)
    {
        var config = new ClientConfig
        {
            ServerUrl = "http://localhost",
            FilePath = Path.Combine(server.DataDirectory, "client.json"),
        };
        var session = new AppSession(config, _ => server.CreateApi());
        var vm = new MainWindowViewModel(session);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, session);
    }

    [AvaloniaFact]
    public async Task Login_then_dashboard_is_shown()
    {
        using var server = new TestServer();
        var (window, vm, _) = Open(server);

        var login = Assert.IsType<LoginViewModel>(vm.Content);
        Assert.NotNull(window.FindDescendantOfType<LoginView>());

        login.Username = "admin";
        login.Password = "admin123";
        await login.LoginCommand.ExecuteAsync(null);

        // Bootstrap admin must change password first.
        var change = Assert.IsType<ChangePasswordViewModel>(vm.Content);
        change.CurrentPassword = "admin123";
        change.NewPassword = change.ConfirmPassword = "newpass1";
        await change.SaveCommand.ExecuteAsync(null);

        var shell = Assert.IsType<ShellViewModel>(vm.Content);
        var dashboard = Assert.IsType<DashboardViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => dashboard.Data is not null);
        Assert.Equal(0, dashboard.Data!.TodaysQuotations);
        Assert.NotNull(window.FindDescendantOfType<ShellView>());
        Assert.NotNull(window.FindDescendantOfType<DashboardView>());
        shell.Dispose();
    }

    [AvaloniaFact]
    public async Task All_navigation_pages_render()
    {
        using var server = new TestServer();
        var (window, vm, session) = Open(server);
        await session.Api.LoginAsync("admin", "admin123");
        var shell = new ShellViewModel(session, () => { });
        vm.Content = shell;

        foreach (var item in shell.NavItems)
        {
            shell.Navigate(item.Key);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var presenter = window.FindDescendantOfType<ShellView>()!;
            Assert.True(item.IsSelected);
            Assert.DoesNotContain(presenter.GetVisualDescendantsText(), t => t.StartsWith("View not found"));
        }

        shell.Navigate("settings");
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => settings.Settings is not null);
        Assert.Equal("TSQ{FY}-{SEQ}", settings.Settings!.Quotation.NumberPattern);
        Assert.StartsWith("TSQ", settings.ExampleNumber);
        shell.Dispose();
    }
}

internal static class VisualExtensions
{
    public static T? FindDescendantOfType<T>(this Control root) where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().FirstOrDefault();

    public static IEnumerable<string> GetVisualDescendantsText(this Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<TextBlock>().Select(t => t.Text ?? "");
}
