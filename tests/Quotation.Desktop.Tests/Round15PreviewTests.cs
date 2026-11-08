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

/// <summary>Screens of Round 15 (pictures only when SCREENSHOT_DIR is set): Won / Lost, WhatsApp, settings, Diagnostics, text size.</summary>
public class Round15PreviewTests
{
    [AvaloniaFact]
    public async Task Round15_screens()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var all = await session.Api.SettingsAsync();
        all.Quotation.TrackOutcomes = true;
        all.Quotation.FollowUpDays = 0;
        all.Quotation.WhatsAppButton = true;
        await session.Api.SaveSettingsAsync(all);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        async Task Shot(string name)
        {
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
        }

        // An approved quotation: Outcome row under the title, WhatsApp in the action bar.
        shell.Navigate("new");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        await editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => editor.ShowOutcome);
        await Shot("d2-e2-approved-quotation");
        editor.OutcomeIndex = (int)QuotationOutcome.Lost;
        editor.LostReason = "Price higher than competitor";
        await Shot("d2-lost-with-reason");
        editor.OutcomeIndex = (int)QuotationOutcome.Open;
        await Task.Delay(500);

        // Dashboard: To Follow Up tile, administrator warnings.
        shell.Navigate("dashboard");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel d && d.HasWarnings && d.ShowFollowUp);
        await Shot("a4-d2-dashboard");
        shell.Navigate("history");
        await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationListViewModel l && l.Rows.Count == 1 && l.TrackOutcomes);
        await Shot("d2-history-outcome");

        // Settings: the administrator switches, and the Backup & updates tab.
        shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => window.FindDescendantOfType<SettingsView>() is not null);
        await Task.Delay(500);
        var tabs = window.FindDescendantOfType<SettingsView>()!.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => t.Header as string == "Quotation");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        var sw = window.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault(t => t.Name == "TrackOutcomesSwitch");
        sw?.BringIntoView();
        await Shot("d2-e2-settings-switches");
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (t.Header as string ?? "").StartsWith("Backup"));
        await Shot("a2-a3-settings-backup-updates");

        // Diagnostics: Export everything / Restore from backup file.
        shell.Navigate("diagnostics");
        await UiHelpers.WaitUntil(() => window.FindDescendantOfType<DiagnosticsView>() is not null);
        await Shot("a5-diagnostics");

        // Text size Large + Compact spacing.
        try
        {
            await session.SetDisplayAsync(textSize: AppDisplay.Large, spacing: AppDisplay.Compact);
            shell.Navigate("mysettings");
            await UiHelpers.WaitUntil(() => window.FindDescendantOfType<MySettingsView>() is not null);
            await Shot("g1-g2-large-compact-mysettings");
            shell.Navigate("new");
            await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
            await Shot("g1-g2-large-compact-quotation");
        }
        finally
        {
            await session.SetDisplayAsync(textSize: AppDisplay.Normal, spacing: AppDisplay.Comfortable);
        }
        shell.Dispose();
        window.Close();
    }
}
