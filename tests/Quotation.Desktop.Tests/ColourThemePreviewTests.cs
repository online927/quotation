using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class ColourThemePreviewTests
{
    [AvaloniaFact]
    public async Task Capture_colour_themes()
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
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
        shell.Navigate("new");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.OtherReferences = "Your enquiry dated 02-Nov-2026";
        editor.FoldHeader();
        foreach (var name in new[] { "universal bevel", "digital caliper" })
        {
            editor.SelectProduct((await session.Api.SearchProductsAsync(name)).First().Product);
            if (editor.EntryRate is null or 0) editor.EntryRate = 100;
            editor.CommitEntryCommand.Execute(null);
        }
        foreach (var theme in Quotation.Contracts.AppTheme.All.Where(t => t != Quotation.Contracts.AppTheme.System))
        {
            ThemeService.Apply(theme);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"theme-{theme}.png"));
        }
        // Glass on the dashboard and the history list too.
        editor.IsDirty = false;
        shell.Navigate("dashboard");
        await Task.Delay(800);
        foreach (var glass in new[] { "GlassBlue", "GlassBlack", "GlassGreen", "GlassMaroon" })
        {
            ThemeService.Apply(glass);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"theme-{glass}-dashboard.png"));
        }
        ThemeService.Apply(Quotation.Contracts.AppTheme.Light);
        shell.Dispose();
        window.Close();
    }
}
