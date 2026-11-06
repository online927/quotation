using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Two PCs on one server: who has it open, save clashes, numbers taken first, recovery after a crash, messages.</summary>
public class MultiUserTests
{
    private sealed class Pc(AppSession session, ShellViewModel shell, MainWindow window, string configDir) : IDisposable
    {
        public AppSession Session { get; } = session;
        public ShellViewModel Shell { get; } = shell;
        public string ConfigDir { get; } = configDir;
        public MainWindow Window { get; } = window;

        /// <summary>Saves a picture of this PC's window when SCREENSHOT_DIR is set.</summary>
        public async Task ShotAsync(string name)
        {
            if (Environment.GetEnvironmentVariable("SCREENSHOT_DIR") is not { Length: > 0 } dir) return;
            Directory.CreateDirectory(dir);
            Window.Width = 1366;
            Window.Height = 768;
            await Task.Delay(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(Window)?.Save(Path.Combine(dir, name + ".png"));
        }
        public void Dispose()
        {
            Shell.Dispose();
            window.Close();
        }
    }

    private static async Task<Pc> PcAsync(TestServer server, string machine, bool sync = false, string? configDir = null)
    {
        var dir = configDir ?? Path.Combine(server.DataDirectory, machine);
        Directory.CreateDirectory(dir);
        var config = new ClientConfig { FilePath = Path.Combine(dir, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi(machine)) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        if (sync) await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main };
        window.Show();
        return new Pc(session, shell, window, dir);
    }

    private static async Task<QuotationEditorViewModel> NewQuotationAsync(Pc pc, string customer = "SONEPAR INDIA PRIVATE")
    {
        pc.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => pc.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)pc.Shell.CurrentPage!;
        await editor.SelectCustomerAsync((await pc.Session.Api.SearchCustomersAsync(customer)).First().Customer.Id);
        editor.SelectProduct((await pc.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        return editor;
    }

    private static async Task<QuotationEditorViewModel> OpenAsync(Pc pc, Guid id)
    {
        pc.Shell.OpenQuotation(id);
        await UiHelpers.WaitUntil(() => pc.Shell.CurrentPage is QuotationEditorViewModel e && e.Id == id && e.Lines.Count > 0);
        return (QuotationEditorViewModel)pc.Shell.CurrentPage!;
    }

    [AvaloniaFact]
    public async Task Someone_else_with_the_quotation_open_is_shown()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        using var a = await PcAsync(server, "PC-A", sync: true);
        using var b = await PcAsync(server, "PC-B");
        var editor = await NewQuotationAsync(a);
        Assert.True(await editor.SaveAsync());
        var id = editor.Id!.Value;

        var other = await OpenAsync(b, id);
        await other.CheckPresenceAsync();
        await editor.CheckPresenceAsync();
        Assert.Contains("(PC-B) also has this quotation open", editor.AlsoOpenText);
        Assert.Contains("(PC-A) also has this quotation open", other.AlsoOpenText);
        await a.ShotAsync("m1-also-open");

        other.Dispose(); // PC-B closes it
        await UiHelpers.WaitUntil(() => { _ = editor.CheckPresenceAsync(); return editor.AlsoOpenText is null; });
    }

    [AvaloniaFact]
    public async Task Save_clash_can_save_mine_as_a_new_quotation()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        using var a = await PcAsync(server, "PC-A", sync: true);
        using var b = await PcAsync(server, "PC-B");
        var first = await NewQuotationAsync(a);
        Assert.True(await first.SaveAsync());
        var id = first.Id!.Value;
        var number = first.Number;

        var theirs = await OpenAsync(b, id);
        theirs.Remarks = "Their change";
        Assert.True(await theirs.SaveAsync());

        first.Remarks = "My change";
        var saving = first.SaveAsync();
        await UiHelpers.WaitUntil(() => a.Shell.PendingDialog is not null);
        Assert.Contains("Save mine as a new quotation", a.Shell.PendingDialog!.PrimaryText);
        await a.ShotAsync("m2-clash-question");
        a.Shell.AnswerDialogCommand.Execute(true);
        Assert.True(await saving);

        Assert.NotEqual(id, first.Id);
        Assert.NotEqual(number, first.Number);
        Assert.Equal("My change", (await a.Session.Api.QuotationAsync(first.Id!.Value)).Remarks);
        Assert.Equal("Their change", (await a.Session.Api.QuotationAsync(id)).Remarks);
    }

    [AvaloniaFact]
    public async Task Save_clash_can_show_their_changes_and_put_mine_back()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        using var a = await PcAsync(server, "PC-A", sync: true);
        using var b = await PcAsync(server, "PC-B");
        var mine = await NewQuotationAsync(a);
        Assert.True(await mine.SaveAsync());
        var id = mine.Id!.Value;

        var theirs = await OpenAsync(b, id);
        theirs.Remarks = "Their change";
        Assert.True(await theirs.SaveAsync());

        mine.Remarks = "My change";
        var saving = mine.SaveAsync();
        await UiHelpers.WaitUntil(() => a.Shell.PendingDialog is not null);
        a.Shell.AnswerDialogCommand.Execute(false); // See their changes
        Assert.False(await saving);
        Assert.Equal("Their change", mine.Remarks);
        Assert.True(mine.HasMyClashVersion);
        await a.ShotAsync("m2-their-changes");

        mine.PutMyChangesBackCommand.Execute(null);
        Assert.Equal("My change", mine.Remarks);
        Assert.True(mine.IsDirty);
        Assert.True(await mine.SaveAsync());
        Assert.Equal("My change", (await a.Session.Api.QuotationAsync(id)).Remarks);
        Assert.Equal(id, mine.Id);
    }

    [AvaloniaFact]
    public async Task Number_taken_by_someone_else_is_explained_on_save()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        using var a = await PcAsync(server, "PC-A", sync: true);
        using var b = await PcAsync(server, "PC-B");
        var mine = await NewQuotationAsync(a);
        var shown = mine.NumberText.Replace(" (provisional)", "");
        var theirs = await NewQuotationAsync(b);
        Assert.True(await theirs.SaveAsync());
        Assert.Equal(shown, theirs.Number);

        Assert.True(await mine.SaveAsync());
        await a.ShotAsync("n5-number-message");
        Assert.NotEqual(shown, mine.Number);
        Assert.Equal($"Saved as {mine.Number}. {shown} was used by another user.", mine.InfoMessage);
        Assert.Contains(a.Shell.Toasts, t => t.Text == mine.InfoMessage); // shown in the corner
    }

    [AvaloniaFact]
    public async Task Unsaved_work_is_offered_back_after_a_crash()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        string dir;
        {
            var a = await PcAsync(server, "PC-A", sync: true);
            dir = a.ConfigDir;
            var editor = await NewQuotationAsync(a);
            editor.Remarks = "Typed before the power cut";
            editor.KeepRecoveryCopy(); // as the 30-second timer does
            Assert.True(File.Exists(Path.Combine(dir, "recovery", "admin.json")));
            a.Dispose(); // the app stops without saving
        }

        using var again = await PcAsync(server, "PC-A", configDir: dir);
        await UiHelpers.WaitUntil(() => again.Shell.PendingDialog is not null);
        Assert.Equal("Restore unsaved work?", again.Shell.PendingDialog!.Title);
        await again.ShotAsync("r7-restore-question");
        again.Shell.AnswerDialogCommand.Execute(true);
        await UiHelpers.WaitUntil(() => again.Shell.CurrentPage is QuotationEditorViewModel { Lines.Count: 1 });
        var restored = (QuotationEditorViewModel)again.Shell.CurrentPage!;
        Assert.Equal("Typed before the power cut", restored.Remarks);
        Assert.Null(restored.Id);
        Assert.True(restored.IsDirty);
        Assert.Contains("provisional", restored.NumberText);

        Assert.True(await restored.SaveAsync());
        Assert.False(File.Exists(Path.Combine(dir, "recovery", "admin.json"))); // saved: the copy is gone
    }

    [AvaloniaFact]
    public async Task Open_pdf_of_a_changed_approved_quotation_asks_to_approve_again()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        using var a = await PcAsync(server, "PC-A", sync: true);
        var editor = await NewQuotationAsync(a);
        await editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => editor.IsFinal);
        Assert.Contains("Approved by admin", editor.ChangedByText);

        editor.Remarks = "Changed after approval";
        var opening = editor.OpenPdfCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => a.Shell.PendingDialog is not null);
        Assert.Equal("Save & approve", a.Shell.PendingDialog!.PrimaryText);
        a.Shell.AnswerDialogCommand.Execute(true);
        await opening;
        Assert.True(editor.IsFinal);
        Assert.False(editor.IsDirty);
        Assert.Equal("Changed after approval", (await a.Session.Api.QuotationAsync(editor.Id!.Value)).Remarks);
    }
}
