using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 17: Cancel forgets unsaved work, the Total band stays inside its box, the Users tab scrolls.</summary>
public class Round17UiTests
{
    private static async Task<(TestServer Server, AppSession Session, MainWindow Window, ShellViewModel Shell)> SetupAsync()
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
        return (server, session, window, shell);
    }

    private static async Task Shot(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public async Task Cancel_on_an_unsaved_quotation_is_not_restored_again()
    {
        var (server, session, window, shell) = await SetupAsync();
        using var _ = server;
        shell.Navigate("new");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        editor.KeepRecoveryCopy(); // as every 30 seconds
        var copy = Path.Combine(server.DataDirectory, "recovery", "admin.json");
        Assert.True(File.Exists(copy));

        await editor.CancelQuotationCommand.ExecuteAsync(null);
        Assert.False(File.Exists(copy)); // nothing comes back at the next start
        shell.Dispose();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Total_band_follows_the_corners_of_its_box_in_every_theme()
    {
        var (server, session, window, shell) = await SetupAsync();
        using var _ = server;
        try
        {
            shell.Navigate("new");
            await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
            Dispatcher.UIThread.RunJobs();
            var band = window.FindDescendantOfType<QuotationEditorView>()!.FindControl<Border>("TotalBand")!;
            var card = band.FindAncestorOfType<Border>()!;
            foreach (var theme in AppTheme.All.Where(t => t != AppTheme.System))
            {
                ThemeService.Apply(theme);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(card.CornerRadius.BottomLeft, band.CornerRadius.BottomLeft);
                Assert.Equal(card.CornerRadius.BottomRight, band.CornerRadius.BottomRight);
                Assert.Equal(0, band.CornerRadius.TopLeft);
            }
            ThemeService.Apply(AppTheme.GlassBlue);
            await Shot(window, "r17-total-band-glass");
            ((QuotationEditorViewModel)shell.CurrentPage!).IsDirty = false;
        }
        finally
        {
            ThemeService.Apply(AppTheme.Light);
        }
        shell.Dispose();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Users_tab_scrolls_and_shows_the_seal_place()
    {
        var (server, session, window, shell) = await SetupAsync();
        using var _ = server;
        await session.Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is SettingsViewModel { Users.Count: 2 });
        var settings = (SettingsViewModel)shell.CurrentPage!;
        var view = window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
        var users = tabs.Items.OfType<TabItem>().First(t => t.Header as string == "Users");
        tabs.SelectedItem = users;
        settings.EditUserCommand.Execute(settings.Users.Single(u => u.Username == "ravi"));
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<ScrollViewer>(users.Content);
        view.FindControl<TabControl>("UserTabs")!.SelectedItem = view.FindControl<TabItem>("UserSealTab"); // Round 21: Seal tab
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        var widthBox = view.FindControl<NumericUpDown>("SealWidthBox")!;
        Assert.True(widthBox.IsEffectivelyVisible); // seal size and place next to the user's seal
        Assert.Null(view.FindControl<Button>("FormerSealButton"));
        widthBox.BringIntoView();
        await Shot(window, "r17-users-seal");
        shell.Dispose();
        window.Close();
    }
}
