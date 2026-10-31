using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Renders Settings → Party Details when SCREENSHOT_DIR is set.</summary>
public class PartyDetailsPreviewTests
{
    [AvaloniaFact]
    public async Task Capture_party_details_settings()
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        using var server = new TestServer(products: 50, customers: 20, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);

        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        shell.Navigate("settings");
        var settings = (SettingsViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => settings.Settings is not null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string?)t.Header == "Party Details");
        await Task.Delay(300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, "settings-party-details.png"));
        await File.WriteAllBytesAsync(Path.Combine(dir, "party-details-default.pdf"),
            await session.Api.PrintPreviewAsync(new QuotationSettings()));
        await File.WriteAllBytesAsync(Path.Combine(dir, "heading-and-half-width.pdf"), await session.Api.PrintPreviewAsync(new QuotationSettings
        {
            QuotationHeading = "PRICE QUOTATION",
            PartyPrint = new PartyPrintSettings { AddressInContinuousLine = true, AddressHalfWidth = true },
        }));
        shell.Dispose();
        window.Close();
    }
}
