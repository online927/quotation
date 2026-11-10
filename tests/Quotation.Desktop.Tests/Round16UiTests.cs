using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Controls;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>
/// Round 16: personal seals (no Seal for administrators), PDF file name order, drag the PDF, WhatsApp follow-up,
/// "Approve" on one line of buttons, solid pop-ups in the glass themes.
/// </summary>
public class Round16UiTests
{
    internal const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAJUlEQVR4nGM4ICDwn5aYYdSCUQtGLRi1YNSCUQtGLRi1YGhYAAC4lDX9H0TkSwAAAABJRU5ErkJggg==";

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

    /// <summary>Signed in as the administrator, or as "ravi" (a user) when <paramref name="asUser"/>.</summary>
    private static async Task<Ctx> SetupAsync(bool asUser = false, int width = 1366, Func<AllSettingsDto, Task>? settings = null,
        Func<Quotation.ApiClient.QuotationApiClient, UserDto, Task>? prepareUser = null)
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        if (settings is not null)
        {
            var all = await session.Api.SettingsAsync();
            await settings(all);
            await session.Api.SaveSettingsAsync(all);
        }
        if (asUser)
        {
            var ravi = await session.Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
            if (prepareUser is not null) await prepareUser(session.Api, ravi);
            await session.Api.LoginAsync("ravi", "ravi-pass");
        }
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = width, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
        return new Ctx { Server = server, Session = session, Window = window, Shell = shell };
    }

    private static async Task<QuotationEditorViewModel> ApprovedAsync(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        await editor.ApproveCommand.ExecuteAsync(null);
        Assert.True(editor.IsFinal);
        return editor;
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
    public async Task Administrators_have_no_seal_box_users_with_a_seal_do()
    {
        using (var admin = await SetupAsync())
        {
            var editor = await ApprovedAsync(admin);
            Assert.False(editor.CanSeal);
            Dispatcher.UIThread.RunJobs();
            Assert.False(admin.Window.FindDescendantOfType<QuotationEditorView>()!.FindControl<ToggleSwitch>("SealSwitch")!.IsVisible);
        }
        using (var noSeal = await SetupAsync(asUser: true))
        {
            Assert.False((await ApprovedAsync(noSeal)).CanSeal);
        }
        using var ctx = await SetupAsync(asUser: true, prepareUser: async (api, ravi) =>
        {
            await api.UploadUserSealAsync(ravi.Id, Convert.FromBase64String(Png));
            await api.SetUserSealPinAsync(ravi.Id, "2020");
        });
        var mine = await ApprovedAsync(ctx);
        Assert.True(mine.CanSeal);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ctx.Window.FindDescendantOfType<QuotationEditorView>()!.FindControl<ToggleSwitch>("SealSwitch")!.IsVisible);
        mine.SealPdf = true;
        var saving = mine.SaveAsPdfCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is { HasInput: true });
        Assert.Equal("Your seal", ctx.Shell.PendingDialog!.Title);
        ctx.Shell.PendingDialog.Input = "2020";
        ctx.Shell.AnswerDialogCommand.Execute(true);
        await saving;
        Assert.Contains("Sealed and saved", mine.InfoMessage);
    }

    [AvaloniaFact]
    public async Task Pdf_file_names_follow_the_users_order()
    {
        Assert.Equal("Sonepar India TSQ2627-5.pdf", QuotationEditorViewModel.PdfFileName("TSQ2627-5", "Sonepar India", customerFirst: true));
        Assert.Equal("TSQ2627-5 Sonepar India.pdf", QuotationEditorViewModel.PdfFileName("TSQ2627-5", "Sonepar India"));
        Assert.Equal("A-B- Co TSQ2627-5.pdf", QuotationEditorViewModel.PdfFileName("TSQ2627-5", "A:B* Co.", customerFirst: true));

        using var ctx = await SetupAsync();
        ctx.Shell.Navigate("mysettings");
        var mine = Assert.IsType<MySettingsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => !mine.IsBusy);
        mine.PdfFileOrderIndex = 1;
        await UiHelpers.WaitUntil(() => ctx.Session.Api.CurrentUser?.PdfFileOrder == PdfFileOrders.CustomerFirst);
        await Shot(ctx.Window, "r16-mysettings-file-name");

        var editor = await ApprovedAsync(ctx);
        Assert.Equal($"Sonepar India Private Limited {editor.Number}.pdf", editor.PdfFileName());
        await editor.SaveAsPdfCommand.ExecuteAsync(null);
        Assert.Contains($"Sonepar India Private Limited {editor.Number}.pdf", ctx.Launcher.Saved);
    }

    [AvaloniaFact]
    public async Task Buttons_of_an_approved_quotation_stay_on_one_line()
    {
        using var ctx = await SetupAsync(width: 1024, settings: s =>
        {
            s.Quotation.WhatsAppButton = true;
            return Task.CompletedTask;
        });
        await ApprovedAsync(ctx);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.Null(view.FindControl<Border>("DragPdfHandle")); // the drag handle is gone (Round 18)

        // "Approve", and every button on one line; on a narrow window the less-used ones show only their icon.
        var bar = view.FindControl<ActionBar>("ActionButtons")!;
        var buttons = bar.Children.OfType<Button>().Where(b => b.IsVisible).ToList();
        Assert.Equal(1, buttons.Select(b => Math.Round(b.Bounds.Center.Y)).Distinct().Count()); // one line: pills and icon tiles share the centre line
        Assert.True(buttons.Max(b => b.Bounds.Right) <= bar.Bounds.Width + 0.5);
        Assert.Contains(buttons, ActionBar.IsIconOnly);
        Assert.False(ActionBar.IsIconOnly(view.FindControl<Button>("ApproveButton")!));
        Assert.False(ActionBar.IsIconOnly(view.FindControl<Button>("SaveAsPdfButton")!));
        await Shot(ctx.Window, "r16-buttons-1024");

        ctx.Window.Width = 1920;
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(bar.Children.OfType<Button>().Where(b => b.IsVisible), ActionBar.IsIconOnly);
        await Shot(ctx.Window, "r16-buttons-1920");
    }

    [AvaloniaFact]
    public async Task Follow_up_list_has_a_whatsapp_button_with_the_follow_up_message()
    {
        using var ctx = await SetupAsync(settings: s =>
        {
            s.Quotation.TrackOutcomes = true;
            s.Quotation.FollowUpDays = 0;
            s.Quotation.WhatsAppButton = true;
            return Task.CompletedTask;
        });
        var editor = await ApprovedAsync(ctx);
        var number = editor.Number!;
        ctx.Shell.Navigate("followup");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationListViewModel { Rows.Count: 1, ShowFollowUpWhatsApp: true });
        var list = (QuotationListViewModel)ctx.Shell.CurrentPage!;
        await Shot(ctx.Window, "r16-follow-up-whatsapp");
        await list.FollowUpWhatsAppCommand.ExecuteAsync(list.Rows[0]);
        var share = Assert.Single(ctx.Launcher.WhatsApp);
        Assert.Equal($"Dear Sir, following up on our quotation {number} dated {DateTime.Today:dd-MMM-yyyy}. Kindly let us know your feedback.",
            share.Message);
        Assert.Equal($"{number} Sonepar India Private Limited.pdf", share.Pdf);

        // Not in other lists, and not when WhatsApp is off.
        var history = new QuotationListViewModel(ctx.Session, ctx.Shell, QuotationListPreset.History);
        await UiHelpers.WaitUntil(() => history.Rows.Count == 1);
        Assert.False(history.ShowFollowUpWhatsApp);
    }

    [AvaloniaFact]
    public async Task Glass_themes_keep_suggestion_lists_and_questions_solid()
    {
        using var ctx = await SetupAsync();
        try
        {
            foreach (var theme in new[] { AppTheme.GlassBlue, AppTheme.GlassBlack, AppTheme.Frost })
            {
                ThemeService.Apply(theme);
                var popup = ctx.Window.FindResource("PopupBrush") as Avalonia.Media.ISolidColorBrush;
                Assert.NotNull(popup);
                Assert.Equal(255, popup!.Color.A);
            }
            ThemeService.Apply(AppTheme.GlassBlue);
            ctx.Shell.Navigate("new");
            await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
            var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
            var picker = ctx.Window.GetVisualDescendants().OfType<SearchPicker>().First();
            picker.GetVisualDescendants().OfType<TextBox>().First().Focus();
            Dispatcher.UIThread.RunJobs();
            ctx.Window.KeyTextInput("sone");
            await Task.Delay(1200);
            Dispatcher.UIThread.RunJobs();
            await Shot(ctx.Window, "r16-glass-suggestions");
            editor.IsDirty = false;
        }
        finally
        {
            ThemeService.Apply(AppTheme.Light);
        }
    }

    [AvaloniaFact]
    public async Task Settings_show_each_users_seal_and_the_seal_place()
    {
        using var ctx = await SetupAsync();
        var ravi = await ctx.Session.Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is SettingsViewModel { Users.Count: 2 });
        var settings = (SettingsViewModel)ctx.Shell.CurrentPage!;
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => t.Header as string == "Users");
        settings.EditUserCommand.Execute(settings.Users.Single(u => u.Username == "ravi"));
        Assert.Equal("Seal: none", SettingsViewModel.SealStateText.Convert(settings.Users.Single(u => u.Username == "ravi"),
            typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));

        // Picture chosen, then the PIN typed and Save user: the seal is ready (no separate "Set PIN").
        await settings.UploadUserSealAsync(Convert.FromBase64String(Png));
        Assert.StartsWith("Picture saved", settings.UserSealState);
        settings.NewUserSealPin = "2020";
        settings.SealWidth = 170;
        await settings.SaveUserCommand.ExecuteAsync(null);
        Assert.Contains("Seal ready", settings.InfoMessage);
        var saved = await ctx.Session.Api.UsersAsync();
        Assert.True(saved.Single(u => u.Username == "ravi").CanSeal);
        Assert.Equal(170, (await ctx.Session.Api.SettingsAsync()).Company.SealWidth);
        Assert.Equal("Seal: ready", SettingsViewModel.SealStateText.Convert(settings.Users.Single(u => u.Username == "ravi"),
            typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));

        settings.EditUserCommand.Execute(settings.Users.Single(u => u.Username == "ravi"));
        await UiHelpers.WaitUntil(() => settings.HasUserSealImage);
        Assert.Equal("Seal ready: this user can seal PDFs.", settings.UserSealState);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        view.FindControl<StackPanel>("UserSealSection")?.BringIntoView();
        await Shot(ctx.Window, "r16-settings-user-seal");

        // An administrator has no seal section.
        settings.EditUserCommand.Execute(settings.Users.Single(u => u.Username == "admin"));
        Assert.False(settings.EditingUserCanHaveSeal);
    }
}
