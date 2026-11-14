using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 21 screens: Cancel after approval, Enter in P&amp;F, Save as PDF opens the PDF, others' quotations, Users page.</summary>
public class Round21UiTests
{
    private sealed class Ctx : IDisposable
    {
        public required TestServer Server { get; init; }
        public required AppSession Session { get; init; }
        public required RecordingLauncher Files { get; init; }
        public required MainWindow Window { get; init; }
        public required ShellViewModel Shell { get; init; }
        public void Dispose()
        {
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    private static async Task<Ctx> SetupAsync(string user, Func<TestServer, QuotationApiClient, Task>? prepare = null)
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        await server.LoginAsStandardAsync("ravi");
        await server.LoginAsStandardAsync("asha");
        if (prepare is not null) await prepare(server, admin);
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var files = new RecordingLauncher(config);
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = files };
        await session.Api.LoginAsync(user, user == "admin" ? "admin123" : user + "-pass");
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel && session.Settings is not null);
        return new Ctx { Server = server, Session = session, Files = files, Window = window, Shell = shell };
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

    private static async Task<Guid> RaviQuotationAsync(TestServer server, bool approve)
    {
        var ravi = server.CreateApi();
        await ravi.LoginAsync("ravi", "ravi-pass");
        var customer = (await ravi.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await ravi.SearchProductsAsync("universal bevel")).First().Product;
        var q = await ravi.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1 }],
        });
        if (approve) await ravi.ApproveQuotationAsync(q.Id);
        return q.Id;
    }

    private static async Task<(QuotationEditorViewModel Vm, QuotationEditorView View)> OpenAsync(Ctx ctx, Guid id)
    {
        ctx.Shell.OpenQuotation(id);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel { Lines.Count: > 0 });
        Dispatcher.UIThread.RunJobs();
        return ((QuotationEditorViewModel)ctx.Shell.CurrentPage!, ctx.Window.FindDescendantOfType<QuotationEditorView>()!);
    }

    [AvaloniaFact]
    public async Task Approved_quotation_has_no_cancel_for_users_and_save_as_pdf_opens_it()
    {
        Guid id = default;
        using var ctx = await SetupAsync("ravi", async (server, _) => id = await RaviQuotationAsync(server, approve: true));
        var (vm, view) = await OpenAsync(ctx, id);
        Assert.False(view.FindControl<Button>("CancelDocButton")!.IsVisible); // approved: only the administrator cancels
        Assert.False(view.FindControl<Button>("OpenPdfButton")!.IsVisible);   // users have Save as PDF (which opens it)
        await Shot(ctx.Window, "r21-user-approved");

        await vm.SaveAsPdfCommand.ExecuteAsync(null);
        Assert.Contains(vm.PdfFileName(), ctx.Files.Opened); // "Open the PDF after Save as PDF" is on at first

        await ctx.Session.Api.SetMyPreferencesAsync(new MyPreferencesDto(OpenPdfAfterSave: false));
        ctx.Session.Api.UpdateCurrentUser(await ctx.Session.Api.MeAsync());
        ctx.Files.Opened.Clear();
        await vm.SaveAsPdfCommand.ExecuteAsync(null);
        Assert.Empty(ctx.Files.Opened);
        Assert.Contains(vm.PdfFileName(), ctx.Files.Saved);
    }

    [AvaloniaFact]
    public async Task Administrator_still_cancels_and_opens_the_pdf()
    {
        Guid id = default;
        using var ctx = await SetupAsync("admin", async (server, _) => id = await RaviQuotationAsync(server, approve: true));
        var (_, view) = await OpenAsync(ctx, id);
        Assert.True(view.FindControl<Button>("CancelDocButton")!.IsVisible);
        Assert.True(view.FindControl<Button>("OpenPdfButton")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task Enter_in_packing_and_forwarding_goes_to_approve_without_pressing_it()
    {
        Guid id = default;
        using var ctx = await SetupAsync("ravi", async (server, _) => id = await RaviQuotationAsync(server, approve: false));
        var (vm, view) = await OpenAsync(ctx, id);
        var packing = view.FindControl<NumericUpDown>("PackingBox")!;
        packing.BringIntoView();
        packing.Focus();
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Button>("ApproveButton")!.IsFocused);
        Assert.Equal(QuotationStatus.Draft, vm.Status); // not approved: Enter again does that
        await Shot(ctx.Window, "r21-pnf-enter-approve");

        // Enter again on the highlighted Approve button approves.
        ctx.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await UiHelpers.WaitUntil(() => vm.Status == QuotationStatus.Generated, 30000);
    }

    [AvaloniaFact]
    public async Task With_the_switch_on_someone_elses_quotation_is_only_looked_at()
    {
        Guid id = default;
        using var ctx = await SetupAsync("asha", async (server, admin) =>
        {
            id = await RaviQuotationAsync(server, approve: false);
            var all = await admin.SettingsAsync();
            all.Quotation.UsersChangeOnlyTheirOwn = true;
            await admin.SaveSettingsAsync(all);
        });
        var (vm, view) = await OpenAsync(ctx, id);
        Assert.False(vm.IsMine);
        Assert.True(view.FindControl<Border>("NotMineNotice")!.IsVisible);
        Assert.False(view.FindControl<Button>("ApproveButton")!.IsVisible);
        Assert.False(view.FindControl<Button>("SaveAsPdfButton")!.IsVisible);
        Assert.False(view.FindControl<Button>("CancelDocButton")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("DocumentContent")!.IsEnabled);
        Assert.True(view.FindControl<Button>("PreviewButton")!.IsVisible);
        await Shot(ctx.Window, "r21-not-mine");

        ctx.Shell.Navigate("history");
        var list = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 1);
        Assert.False(list.Rows[0].CanDuplicate);
    }

    [AvaloniaFact]
    public async Task Users_page_people_list_and_tabs()
    {
        using var ctx = await SetupAsync("admin");
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is SettingsViewModel { Users.Count: 3 });
        var settings = (SettingsViewModel)ctx.Shell.CurrentPage!;
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => t.Header as string == "Users");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.ShowUserHint);
        await Shot(ctx.Window, "r21-users-empty");

        settings.SelectedUser = settings.FilteredUsers.Single(u => u.Username == "ravi");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ravi", settings.EditUsername);
        Assert.True(view.FindControl<StackPanel>("UserDetails")!.IsEffectivelyVisible);
        await Shot(ctx.Window, "r21-users-account");
        view.FindControl<TabControl>("UserTabs")!.SelectedItem = view.FindControl<TabItem>("UserSealTab");
        await Shot(ctx.Window, "r21-users-seal");
        view.FindControl<TabControl>("UserTabs")!.SelectedItem = view.FindControl<TabItem>("UserPasswordTab");
        await Shot(ctx.Window, "r21-users-password");

        settings.UserFilter = "ash";
        Assert.Equal(["asha"], settings.FilteredUsers.Select(u => u.Username));
        settings.UserFilter = "";

        settings.StartAddUserCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<StackPanel>("AddUserForm")!.IsEffectivelyVisible);
        await Shot(ctx.Window, "r21-users-add");
        settings.NewUsername = "keerthana";
        settings.NewDisplayName = "Keerthana S";
        settings.NewUserPassword = "kee1";
        await settings.AddUserCommand.ExecuteAsync(null);
        Assert.Equal("keerthana", settings.SelectedUser?.Username); // straight on to the new person
        Assert.False(settings.IsAddingUser);
    }
}
