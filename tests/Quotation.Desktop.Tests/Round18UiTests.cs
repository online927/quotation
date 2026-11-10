using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 18 screens: the administrator only looks, Standard users, separate Contact / E-mail ticks, empty Rate box, proforma switch.</summary>
public class Round18UiTests
{
    private sealed class Ctx : IDisposable
    {
        public required TestServer Server { get; init; }
        public required AppSession Session { get; init; }
        public required MainWindow Window { get; init; }
        public required ShellViewModel Shell { get; init; }
        public void Dispose()
        {
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    /// <summary>A server with the real rule (the administrator makes no quotations), signed in as <paramref name="user"/>.</summary>
    private static async Task<Ctx> SetupAsync(string user = "admin", Func<TestServer, QuotationApiClient, Task>? prepare = null)
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        if (prepare is not null) await prepare(server, admin);
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        if (user == "admin") await session.Api.LoginAsync("admin", "admin123");
        else await session.Api.LoginAsync(user, user + "-pass");
        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel && session.Settings is not null);
        return new Ctx { Server = server, Session = session, Window = window, Shell = shell };
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

    private static async Task<SaveQuotationRequest> RequestAsync(QuotationApiClient api)
    {
        var customer = (await api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await api.SearchProductsAsync("universal bevel")).First().Product;
        return new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1 }],
        };
    }

    [AvaloniaFact]
    public async Task Administrator_opens_cancels_and_deletes_but_makes_no_quotations()
    {
        Guid id = default;
        using var ctx = await SetupAsync(prepare: async (server, _) =>
        {
            var ravi = await server.LoginAsStandardAsync();
            id = (await ravi.CreateQuotationAsync(await RequestAsync(ravi))).Id;
        });
        Assert.False(ctx.Session.MakesDocuments);
        Assert.DoesNotContain(ctx.Shell.NavItems, n => n.Key is "new" or "newproforma" or "proforma");
        Assert.Equal("Admin", ctx.Shell.UserRole);
        ctx.Shell.NewQuotation(); // Ctrl+N does nothing
        Assert.IsType<DashboardViewModel>(ctx.Shell.CurrentPage);
        await Shot(ctx.Window, "r18-admin-dashboard");

        ctx.Shell.Navigate("history");
        var list = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 1);
        Dispatcher.UIThread.RunJobs();
        var listView = ctx.Window.FindDescendantOfType<QuotationListView>()!;
        Assert.False(listView.FindControl<Button>("NewDocButton")!.IsVisible);
        Assert.False(listView.FindControl<ComboBox>("TypeFilterBox")!.IsVisible); // proforma invoices are off
        Assert.DoesNotContain(listView.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Duplicate" && b.IsEffectivelyVisible);
        await Shot(ctx.Window, "r18-admin-history");

        ctx.Shell.OpenQuotation(id);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel { Lines.Count: 1 });
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.False(editor.CanEditContent);
        Assert.False(view.FindControl<StackPanel>("DocumentContent")!.IsEnabled);
        Assert.False(view.FindControl<Button>("ApproveButton")!.IsVisible);
        Assert.False(view.FindControl<Button>("SaveAsPdfButton")!.IsVisible);
        Assert.True(view.FindControl<Button>("PreviewButton")!.IsVisible);
        Assert.True(view.FindControl<Button>("CancelDocButton")!.IsEffectivelyEnabled);
        Assert.True(view.FindControl<Button>("DeleteButton")!.IsVisible);
        Assert.False(await editor.SaveAsync()); // Ctrl+S does nothing
        await Shot(ctx.Window, "r18-admin-quotation");

        await editor.CancelQuotationCommand.ExecuteAsync(null);
        Assert.Equal(QuotationStatus.Cancelled, editor.Status);
    }

    [AvaloniaFact]
    public async Task Standard_user_sees_an_empty_rate_box_when_there_is_no_rate()
    {
        using var ctx = await SetupAsync("ravi", async (server, _) => await server.LoginAsStandardAsync());
        Assert.True(ctx.Session.MakesDocuments);
        Assert.Equal("Standard", ctx.Shell.UserRole);
        Assert.Contains(ctx.Shell.NavItems, n => n.Key == "new");
        Assert.DoesNotContain(ctx.Shell.NavItems, n => n.Key is "newproforma" or "proforma");

        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
        editor.SelectProduct(product with { Rate = null }); // an item with no rate in Tally
        Dispatcher.UIThread.RunJobs();
        Assert.Null(editor.EntryRate);
        var rateBox = ctx.Window.FindDescendantOfType<QuotationEditorView>()!.FindControl<NumericUpDown>("RateBox")!;
        Assert.Null(rateBox.Value);
        Assert.True(string.IsNullOrEmpty(rateBox.Text));
        await Shot(ctx.Window, "r18-empty-rate");

        editor.EntryQuantity = 1;
        editor.CommitEntryCommand.Execute(null);
        Assert.Equal("", editor.Lines[0].RateText); // the line shows no 0.00 either
        editor.EditLineCommand.Execute(editor.Lines[0]);
        Assert.Null(editor.EntryRate);
        editor.CancelEntryCommand.Execute(null);
        editor.IsDirty = false;
    }

    [AvaloniaFact]
    public async Task Settings_one_administrator_contact_and_email_ticks_and_proforma_switch()
    {
        using var ctx = await SetupAsync(prepare: async (_, admin) =>
            await admin.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User)));
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is SettingsViewModel { Users.Count: 2 });
        var settings = (SettingsViewModel)ctx.Shell.CurrentPage!;
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
        TabItem Tab(string header) => tabs.Items.OfType<TabItem>().First(t => t.Header as string == header);

        // Users: no Administrator tick box; "Standard" in the list.
        tabs.SelectedItem = Tab("Users");
        settings.EditUserCommand.Execute(settings.Users.Single(u => u.Username == "ravi"));
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<CheckBox>(), c => c.Content as string is "Administrator" or "Admin");
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Standard" && t.IsEffectivelyVisible);
        await Shot(ctx.Window, "r18-users-standard");

        // Party Details: Contact and E-mail have their own tick boxes (both start ticked, as before).
        tabs.SelectedItem = Tab("Party Details");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        var contact = view.FindControl<CheckBox>("ShowContactBox")!;
        var email = view.FindControl<CheckBox>("ShowEmailBox")!;
        Assert.True(contact.IsChecked);
        Assert.True(email.IsChecked);
        email.IsChecked = false;
        Assert.False(settings.Settings!.Quotation.PartyPrint.ShowEmail);
        Assert.True(settings.Settings.Quotation.PartyPrint.ShowContactDetails);
        await Shot(ctx.Window, "r18-party-contact-email");

        // Quotation: proforma invoices off at first; switching on shows them in the menu after Save.
        tabs.SelectedItem = Tab("Quotation");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        var proforma = view.FindControl<ToggleSwitch>("ProformaSwitch")!;
        Assert.False(proforma.IsChecked);
        proforma.BringIntoView();
        await Shot(ctx.Window, "r18-proforma-switch-off");
        proforma.IsChecked = true;
        await settings.SaveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.NavItems.Any(n => n.Key == "proforma"));
        Assert.DoesNotContain(ctx.Shell.NavItems, n => n.Key == "newproforma"); // the administrator still makes none
        Assert.False((await ctx.Session.Api.SettingsAsync()).Quotation.PartyPrint.ShowEmail);
        await Shot(ctx.Window, "r18-proforma-switch-on");
    }
}
