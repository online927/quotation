using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>U1: the side menu folds to icons, and each PC remembers it.</summary>
public class SidebarTests
{
    [AvaloniaFact]
    public async Task Side_menu_folds_to_icons_and_is_remembered_on_this_pc()
    {
        using var server = new TestServer(products: 20, customers: 10, today: DateOnly.FromDateTime(DateTime.Today));
        var configPath = Path.Combine(server.DataDirectory, "c.json");
        var config = new ClientConfig { FilePath = configPath };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
        var view = window.FindDescendantOfType<ShellView>()!;
        var bar = view.FindControl<Border>("Sidebar")!;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(262, bar.Bounds.Width);
        Assert.NotNull(window.Icon); // the black-and-gold box icon (Assets/app.ico)

        view.FindControl<Button>("SidebarToggle")!.Command!.Execute(null);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.SidebarCollapsed);
        Assert.Equal(64, bar.Bounds.Width);
        // Only icons: the names are hidden, each button still says its name on mouse-over.
        var nav = bar.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("nav") && b.Name != "SidebarToggle");
        Assert.False(nav.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("navgrid")).IsEffectivelyVisible);
        Assert.Equal("Dashboard", ToolTip.GetTip(nav));
        Assert.True(ClientConfig.Load(configPath).SidebarCollapsed);

        shell.Dispose();
        window.Close();
        var again = new ShellViewModel(session, () => { });
        Assert.True(again.SidebarCollapsed); // the next start on this PC
        again.ToggleSidebarCommand.Execute(null);
        Assert.False(ClientConfig.Load(configPath).SidebarCollapsed);
        again.Dispose();
    }
}
