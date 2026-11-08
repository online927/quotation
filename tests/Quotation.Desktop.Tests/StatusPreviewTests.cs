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

/// <summary>The status dot in the top bar and the warning strip (no status bar at the bottom).</summary>
public class StatusPreviewTests
{
    [AvaloniaFact]
    public async Task Status_dot_and_warning_strip()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.Status is not null && shell.SpeedText.Contains("ms"));

        Assert.False(shell.HealthBad); // green, or orange if this PC answers slowly
        Assert.Null(shell.WarningText);
        Assert.Contains("Tally: connected", shell.HealthDetails);
        Assert.Equal("Connected", shell.HealthLabel);
        Assert.DoesNotContain("App v", shell.HealthDetails);
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        async Task Shot(string name)
        {
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
        }
        shell.Navigate("new");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        await Shot("s1-all-well");

        var chip = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "StatusChip");
        chip.Flyout!.ShowAt(chip);
        await Shot("s2-details");
        chip.Flyout.Hide();

        shell.Status = shell.Status! with { TallyConnected = false };
        Assert.True(shell.HealthBad);
        Assert.Equal("Disconnected", shell.HealthLabel);
        Assert.StartsWith("Tally disconnected", shell.WarningText);
        await Shot("s3-tally-disconnected");

        shell.ServerReachable = false;
        Assert.StartsWith("Quotation Server unreachable", shell.WarningText);
        await Shot("s4-server-unreachable");
        shell.CloseWarningCommand.Execute(null);
        Assert.Null(shell.VisibleWarning);
        Assert.True(shell.HealthBad); // the dot stays red
        await Shot("s4b-closed");
        shell.ServerReachable = true; // server back, Tally still off: a different problem shows again
        Assert.StartsWith("Tally disconnected", shell.VisibleWarning);

        ((QuotationEditorViewModel)shell.CurrentPage!).IsDirty = false;
        shell.ServerReachable = true;
        shell.Navigate("mysettings");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is MySettingsViewModel { FinancialYear: not "…" });
        await Task.Delay(200);
        foreach (var sv in window.GetVisualDescendants().OfType<ScrollViewer>()) sv.ScrollToEnd();
        await Shot("s5-my-settings");
        shell.Dispose();
        window.Close();
    }
}
