using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>
/// Round 15 on the screens: the "line 2 deleted" fix with Undo, Log out question, Won / Lost, WhatsApp, To Follow Up,
/// automatic updates, health warnings, Export everything and the appearance settings.
/// </summary>
public class Round15UiTests
{
    private sealed class Ctx : IDisposable
    {
        public required TestServer Server;
        public required AppSession Session;
        public required MainWindow Window;
        public required ShellViewModel Shell;
        public RecordingLauncher Launcher => (RecordingLauncher)Session.Documents;
        public void Dispose()
        {
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    private static async Task<Ctx> SetupAsync(Func<AllSettingsDto, Task>? settings = null, Action<TestServer>? before = null)
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        before?.Invoke(server);
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi());
        session.Documents = new RecordingLauncher(config);
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        if (settings is not null)
        {
            var all = await session.Api.SettingsAsync();
            await settings(all);
            await session.Api.SaveSettingsAsync(all);
        }
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
        return new Ctx { Server = server, Session = session, Window = window, Shell = shell };
    }

    private static async Task<QuotationEditorViewModel> EditorWithTwoLinesAsync(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("530-104")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        Assert.Equal(2, editor.Lines.Count);
        return editor;
    }

    [AvaloniaFact]
    public async Task Editing_line_1_goes_to_quantity_and_a_removed_line_can_be_undone()
    {
        using var ctx = await SetupAsync();
        var editor = await EditorWithTwoLinesAsync(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Dispatcher.UIThread.RunJobs();

        // The Edit and remove buttons on the lines never keep the keyboard: Space after Edit cannot press "remove" on line 2.
        var rowButtons = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content as string == "Edit" || ToolTip.GetTip(b) as string == "Remove this line").ToList();
        Assert.NotEmpty(rowButtons);
        Assert.All(rowButtons, b => Assert.False(b.Focusable));

        editor.EditLineCommand.Execute(editor.Lines[0]);
        await UiHelpers.WaitUntil(() => view.FindControl<NumericUpDown>("QtyBox")!.IsKeyboardFocusWithin);
        ctx.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, editor.Lines.Count);

        // Removing a line shows "Line 2 removed — Undo"; Undo puts it back in its place.
        var second = editor.Lines[1];
        editor.RemoveLineCommand.Execute(second);
        Assert.Single(editor.Lines);
        var toast = ctx.Shell.Toasts.Last();
        Assert.StartsWith("Line 2 removed", toast.Text);
        Assert.Equal("Undo", toast.ActionText);
        ctx.Shell.ToastActionCommand.Execute(toast);
        Assert.Equal(2, editor.Lines.Count);
        Assert.Same(second, editor.Lines[1]);
        Assert.Equal(2, second.LineNo);
    }

    [AvaloniaFact]
    public async Task Log_out_asks_first_and_keeps_unsaved_work()
    {
        using var ctx = await SetupAsync();
        await EditorWithTwoLinesAsync(ctx);
        var leaving = ctx.Shell.LogoutCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        Assert.Equal("Are you logging out, bro?", ctx.Shell.PendingDialog!.Title);
        Assert.Equal("Yes", ctx.Shell.PendingDialog.PrimaryText);
        Assert.Equal("No", ctx.Shell.PendingDialog.SecondaryText);
        ctx.Shell.AnswerDialogCommand.Execute(false);
        await leaving;
        Assert.NotNull(ctx.Session.Api.CurrentUser); // still signed in

        leaving = ctx.Shell.LogoutCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        ctx.Shell.AnswerDialogCommand.Execute(true);
        await leaving;
        Assert.True(File.Exists(Path.Combine(ctx.Server.DataDirectory, "recovery", "admin.json")));
    }

    [AvaloniaFact]
    public async Task Won_lost_and_whatsapp_appear_only_when_switched_on()
    {
        // Off (the default): no outcome row, no WhatsApp button, no follow-up tile.
        using (var off = await SetupAsync())
        {
            var editor = await EditorWithTwoLinesAsync(off);
            await editor.ApproveCommand.ExecuteAsync(null);
            Assert.True(editor.IsFinal);
            Assert.False(editor.ShowOutcome);
            Assert.False(editor.ShowWhatsApp);
            var dash = new DashboardViewModel(off.Session, _ => { });
            await UiHelpers.WaitUntil(() => dash.Data is not null);
            Assert.False(dash.ShowFollowUp);
        }

        using var ctx = await SetupAsync(s =>
        {
            s.Quotation.TrackOutcomes = true;
            s.Quotation.FollowUpDays = 0; // due today, for the To Follow Up list
            s.Quotation.WhatsAppButton = true;
            s.Quotation.WhatsAppMessage = "Dear Sir, quotation {number} for {customer} attached.";
            return Task.CompletedTask;
        });
        var q = await EditorWithTwoLinesAsync(ctx);
        Assert.False(q.ShowOutcome); // not approved yet
        await q.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => q.ShowOutcome);
        Assert.True(q.ShowWhatsApp);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.True(view.FindControl<StackPanel>("OutcomeRow")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("WhatsAppButton")!.IsEffectivelyVisible);
        Assert.Equal(DateTime.Today, q.FollowUpDate); // approval + 0 days

        // WhatsApp: PDF saved, copied and WhatsApp opened with the message filled in.
        await q.WhatsAppCommand.ExecuteAsync(null);
        var share = Assert.Single(ctx.Launcher.WhatsApp);
        Assert.Equal($"Dear Sir, quotation {q.Number} for Sonepar India Private Limited attached.", share.Message);
        Assert.EndsWith(".pdf", share.Pdf);

        // To Follow Up: on the dashboard and in its own list until it is marked Won.
        var dashboard = new DashboardViewModel(ctx.Session, _ => { });
        await UiHelpers.WaitUntil(() => dashboard.Data is not null);
        Assert.True(dashboard.ShowFollowUp);
        Assert.Equal(1, dashboard.Data!.FollowUpDue);
        var list = new QuotationListViewModel(ctx.Session, ctx.Shell, QuotationListPreset.FollowUp);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 1);
        Assert.Equal("To Follow Up", list.Title);

        q.OutcomeIndex = (int)QuotationOutcome.Lost;
        Assert.True(q.IsLost);
        q.LostReason = "Price";
        q.SaveLostReasonCommand.Execute(null);
        await WaitServer(async () => (await ctx.Session.Api.QuotationAsync(q.Id!.Value)).LostReason == "Price");
        q.OutcomeIndex = (int)QuotationOutcome.Won;
        await WaitServer(async () => (await ctx.Session.Api.QuotationAsync(q.Id!.Value)).Outcome == QuotationOutcome.Won);
        await list.LoadAsync();
        Assert.Empty(list.Rows);

        // History: the outcome filter and column.
        var history = new QuotationListViewModel(ctx.Session, ctx.Shell, QuotationListPreset.History);
        await UiHelpers.WaitUntil(() => history.Rows.Count == 1);
        Assert.True(history.ShowOutcomeFilter);
        Assert.Equal("Won", history.Rows[0].Outcome);
        history.OutcomeFilter = QuotationListViewModel.OutcomeFilters.Single(f => f.Outcome == QuotationOutcome.Lost);
        await UiHelpers.WaitUntil(() => history.Rows.Count == 0);
    }

    [AvaloniaFact]
    public async Task A_newer_client_on_the_server_is_offered_and_installed()
    {
        var oldVersion = ShellViewModel.CurrentVersion;
        var oldExit = ShellViewModel.ExitApp;
        var exited = 0;
        ShellViewModel.CurrentVersion = new Version(0, 9, 1, 0);
        ShellViewModel.ExitApp = () => exited++;
        try
        {
            using var ctx = await SetupAsync(before: server =>
            {
                Directory.CreateDirectory(server.ClientUpdateFolder);
                File.WriteAllBytes(Path.Combine(server.ClientUpdateFolder, "TSQuotation-Setup.exe"), new byte[2048]);
                File.WriteAllText(Path.Combine(server.ClientUpdateFolder, "version.txt"), "0.9.77");
            });
            await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
            Assert.Equal("New version 0.9.77 is available", ctx.Shell.PendingDialog!.Title);
            Assert.Equal("Update now", ctx.Shell.PendingDialog.PrimaryText);
            Assert.Equal("Later", ctx.Shell.PendingDialog.SecondaryText);
            await Shot(ctx.Window, "a2-update-question");
            ctx.Shell.AnswerDialogCommand.Execute(true);
            await UiHelpers.WaitUntil(() => exited == 1);
            var setup = Assert.Single(ctx.Launcher.Installers);
            Assert.Equal(2048, new FileInfo(setup).Length);

            // Same version: nothing is asked.
            ShellViewModel.CurrentVersion = new Version(0, 9, 77, 0);
            await ctx.Shell.CheckForUpdateAsync();
            Assert.Null(ctx.Shell.PendingDialog);

            // "Clients must update": no Later, only Close app.
            ShellViewModel.CurrentVersion = new Version(0, 9, 1, 0);
            var all = await ctx.Session.Api.SettingsAsync();
            all.Backup.ClientsMustUpdate = true;
            await ctx.Session.Api.SaveSettingsAsync(all);
            var asking = ctx.Shell.CheckForUpdateAsync();
            await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
            Assert.Equal("Close app", ctx.Shell.PendingDialog!.SecondaryText);
            ctx.Shell.AnswerDialogCommand.Execute(false);
            await asking;
            Assert.Equal(2, exited);
        }
        finally
        {
            ShellViewModel.CurrentVersion = oldVersion;
            ShellViewModel.ExitApp = oldExit;
        }
    }

    [AvaloniaFact]
    public async Task Dashboard_warns_administrators_and_diagnostics_exports_everything()
    {
        using var ctx = await SetupAsync();
        var dashboard = (DashboardViewModel)ctx.Shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => dashboard.HasWarnings);
        Assert.Contains(dashboard.Warnings, w => w.Text.Contains("No database backup"));
        Assert.Contains(dashboard.Warnings, w => w.Text.Contains("second backup copy"));
        await Shot(ctx.Window, "a4-dashboard-warnings");

        var diagnostics = new DiagnosticsViewModel(ctx.Session, ctx.Shell, () => { })
        {
            ExportFolder = Path.Combine(ctx.Server.DataDirectory, "export-here"),
        };
        await diagnostics.ExportCommand.ExecuteAsync(null);
        var file = Assert.Single(Directory.GetFiles(diagnostics.ExportFolder, "*.tsq"));
        Assert.True(new FileInfo(file).Length > 0);
        Assert.Contains("exported", diagnostics.InfoMessage);
    }

    [AvaloniaFact]
    public async Task Text_size_spacing_and_animations_follow_the_user()
    {
        using var ctx = await SetupAsync();
        try
        {
            await ctx.Session.SetDisplayAsync(textSize: AppDisplay.Large, spacing: AppDisplay.Compact, reduceMotion: true);
            Assert.Equal(1.15, ThemeService.Zoom, 3);
            Assert.False(ThemeService.AnimationsOn);
            Assert.Equal(6.0, Avalonia.Application.Current!.Resources["SectionSpacing"]);
            var me = await ctx.Session.Api.MeAsync();
            Assert.Equal(AppDisplay.Large, me.TextSize);
            Assert.Equal(AppDisplay.Compact, me.Spacing);
            Assert.True(me.ReduceMotion);
            var zoom = ctx.Window.FindControl<LayoutTransformControl>("Zoom")!;
            Assert.Equal(1.15, ((Avalonia.Media.ScaleTransform)zoom.LayoutTransform!).ScaleX, 3);
        }
        finally
        {
            await ctx.Session.SetDisplayAsync(textSize: AppDisplay.Normal, spacing: AppDisplay.Comfortable, reduceMotion: false);
        }
        Assert.Equal(1.0, ThemeService.Zoom, 3);
        Assert.True(ThemeService.AnimationsOn);
    }

    private static async Task WaitServer(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition()) return;
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Fail("Timed out waiting for the server.");
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
}
