using Avalonia;
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

/// <summary>Round 38: eye button (EY-1), F9 sync (F9-1), line shortcuts (Q5), product link icon (PL-1), keys in question
/// boxes (EN-1), working while the server is off (Q10), delivery list (DM-1).</summary>
public class Round38UiTests
{
    private static async Task Pump(int rounds = 10)
    {
        for (var i = 0; i < rounds; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
    }

    private static void Press(Window window, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPress(key, mods, PhysicalKey.None, null);
        window.KeyRelease(key, mods, PhysicalKey.None, null);
    }

    private static async Task<QuotationEditorViewModel> WithTwoLinesAsync(Round33UiTests.Ctx ctx)
    {
        var (editor, _) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.True(await editor.OpenPastedListAsync("187-901-10 x 1\n530-104 x 2"));
        await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
        editor.AddPastedLinesCommand.Execute(null);
        await Pump();
        return editor;
    }

    [AvaloniaFact]
    public async Task The_hide_eye_of_the_open_customer_section_sits_in_the_references_box()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        editor.HeaderCollapsed = false;
        await Pump();
        var eye = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "HideHeaderButton");
        var bar = eye.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("headbar"));
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "References");
        Assert.DoesNotContain(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Buyer (Bill to)");
    }

    [AvaloniaFact]
    public async Task F9_anywhere_syncs_with_tally_and_says_so()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var editor = await WithTwoLinesAsync(ctx);
        Press(ctx.Window, Key.F9);
        await UiHelpers.WaitUntil(() => ctx.Shell.Toasts.Any(t => t.Text.StartsWith("Tally sync done", StringComparison.Ordinal)));
        Assert.Contains(ctx.Shell.Toasts, t => t.Text == "Syncing with Tally…");
        Assert.Equal(2, editor.Lines.Count);                                    // the quotation is not touched
        Assert.Contains(ctx.Shell.KeyBar, k => k.Keys == "F9");
    }

    [AvaloniaFact]
    public async Task Ctrl_D_copies_the_line_below_and_Ctrl_arrows_move_it_with_undo()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var editor = await WithTwoLinesAsync(ctx);
        var first = editor.Lines[0];
        editor.EditLineCommand.Execute(first);
        await Pump();
        Press(ctx.Window, Key.D, RawInputModifiers.Control);
        await Pump();
        Assert.Equal(3, editor.Lines.Count);
        Assert.Equal(first.ItemName, editor.Lines[1].ItemName);
        Assert.Equal(first.Rate, editor.Lines[1].Rate);
        Assert.Same(editor.Lines[1], editor.EditingLine);                       // the copy is ready to change
        ctx.Shell.Toasts.Last().Action!();
        Assert.Equal(2, editor.Lines.Count);

        editor.EditLineCommand.Execute(editor.Lines[0]);
        await Pump();
        var moving = editor.Lines[0];
        Press(ctx.Window, Key.Down, RawInputModifiers.Control);
        await Pump();
        Assert.Same(moving, editor.Lines[1]);
        Assert.Equal(2, moving.LineNo);
        Press(ctx.Window, Key.Down, RawInputModifiers.Control);                 // already last: stays
        await Pump();
        Assert.Same(moving, editor.Lines[1]);
        Press(ctx.Window, Key.Up, RawInputModifiers.Control);
        await Pump();
        Assert.Same(moving, editor.Lines[0]);
    }

    [AvaloniaFact]
    public async Task An_item_with_a_product_link_shows_a_small_link_icon_that_opens_it()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var admin = await ctx.Server.LoginAsAdminAsync("ADMIN-PC");
        await admin.LoadProductLinksAsync([new ProductLinkRowDto("187-901-10", "https://www.example.com/bevel")]);
        var editor = await WithTwoLinesAsync(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var icons = view.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "ProductLinkButton").ToList();
        Assert.Equal(2, icons.Count);
        Assert.Single(icons, b => b.IsVisible);
        Assert.Null(ToolTip.GetTip(icons.Single(b => b.IsVisible)));          // no hover box
        icons.Single(b => b.IsVisible).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["https://www.example.com/bevel"], ((RecordingLauncher)ctx.Session.Documents).WebPages);
        Assert.True(editor.ShowProductLinks);
        Assert.True(view.FindControl<CheckBox>("ShowProductLinksBox")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task Tab_goes_round_inside_a_question_box_and_never_behind_it()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var asking = ctx.Shell.ConfirmAsync("New version 1.0 is available", "Update now?", "Update now", "Later");
        await Pump();
        Control? Focused() => ctx.Window.FocusManager?.GetFocusedElement() as Control;
        Assert.Equal("DialogPrimary", Focused()?.Name);
        Press(ctx.Window, Key.Tab);
        await Pump();
        Assert.Equal("DialogSecondary", Focused()?.Name);
        Press(ctx.Window, Key.Tab);
        await Pump();
        Assert.Equal("DialogPrimary", Focused()?.Name);
        Press(ctx.Window, Key.Tab, RawInputModifiers.Shift);
        await Pump();
        Assert.Equal("DialogSecondary", Focused()?.Name);
        Press(ctx.Window, Key.Enter);                                           // the focused button answers: Later
        await Pump();
        Assert.True(asking.IsCompleted);
        Assert.False(await asking);
    }

    [AvaloniaFact]
    public async Task Tab_goes_round_the_three_buttons_of_the_unsaved_changes_question()
    {
        using var ctx = await Round33UiTests.StartAsync();
        await WithTwoLinesAsync(ctx);
        ctx.Shell.Navigate("dashboard");
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingPrompt is not null);
        await Pump();
        Control? Focused() => ctx.Window.FocusManager?.GetFocusedElement() as Control;
        Assert.Equal("PromptSave", Focused()?.Name);
        var seen = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            Press(ctx.Window, Key.Tab);
            await Pump();
            seen.Add(Focused()?.Name);
        }
        Assert.Equal(["PromptDontSave", "PromptCancel", "PromptSave"], seen);
        Press(ctx.Window, Key.Escape);
        await Pump();
        Assert.Null(ctx.Shell.PendingPrompt);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_user_boxes_adds_or_saves_the_user()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is SettingsViewModel);
        await Pump();
        var settings = (SettingsViewModel)ctx.Shell.CurrentPage!;
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        foreach (var (box, command) in new[] { ("NewUsernameBox", settings.AddUserCommand), ("NewUserPasswordBox", settings.AddUserCommand),
                     ("EditUsernameBox", settings.SaveUserCommand), ("UserSealPinBox", settings.SaveUserCommand) })
        {
            var text = view.FindControl<TextBox>(box)!;
            Assert.Contains(text.KeyBindings, k => k.Gesture.Key == Key.Enter && ReferenceEquals(k.Command, command));
        }
    }

    [AvaloniaFact]
    public async Task The_delivery_list_offers_only_the_usual_times()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, _) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.Equal([QuotationEditorViewModel.NoDelivery, .. QuotationEditorViewModel.StandardDeliveryTimes], editor.DeliveryChoices);
    }

    /// <summary>Lets every request fail as if the server PC were switched off.</summary>
    private sealed class Switch : DelegatingHandler
    {
        public bool Off { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Off ? throw new HttpRequestException("No connection could be made") : base.SendAsync(request, ct);
    }

    [AvaloniaFact]
    public async Task With_the_server_off_saving_waits_on_this_pc_and_sends_itself_once_when_it_is_back()
    {
        var oldRetry = QuotationEditorViewModel.SendRetryInterval;
        QuotationEditorViewModel.SendRetryInterval = TimeSpan.FromMilliseconds(200);
        var cable = new Switch();
        try
        {
            using var server = new TestServer(products: 300, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
            var admin = await server.LoginAsAdminAsync();
            await admin.StartSyncAsync(SyncKind.Full, wait: true);
            await server.LoginAsStandardAsync("ravi");
            var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
            var session = new AppSession(config, _ => new QuotationApiClient(server.CreateDefaultClient(cable), "TEST-PC"))
                { Documents = new RecordingLauncher(config) };
            await session.Api.LoginAsync("ravi", "ravi-pass");
            using var shell = new ShellViewModel(session, () => { });
            var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
            window.Show();
            await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
            shell.Navigate("new");
            var editor = Assert.IsType<QuotationEditorViewModel>(shell.CurrentPage);
            await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
            await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
            editor.SelectProduct((await session.Api.SearchProductsAsync("187-901-10")).First().Product);
            editor.EntryQuantity = 1;
            editor.CommitEntryCommand.Execute(null);

            cable.Off = true;                                                   // the server PC is switched off
            Assert.False(await editor.SaveAsync());
            Assert.True(editor.WaitingToSend);
            Assert.Null(editor.ErrorMessage);
            Assert.True(editor.IsDirty);
            await Pump();
            Assert.True(window.FindDescendantOfType<QuotationEditorView>()!.FindControl<Border>("WaitingToSendStrip")!.IsVisible);
            Assert.NotNull(RecoveryStore.For(config).Load("ravi"));             // kept on this PC
            editor.Remarks = "Typed while the server was off";                 // typing goes on

            cable.Off = false;                                                  // back: sent by itself
            await UiHelpers.WaitUntil(() => !editor.WaitingToSend, 10000);
            Assert.NotNull(editor.Id);
            Assert.Contains(shell.Toasts, t => t.Text.StartsWith("Server is back", StringComparison.Ordinal));
            var saved = await session.Api.QuotationAsync(editor.Id!.Value);
            Assert.Equal("Typed while the server was off", saved.Remarks);
            Assert.Equal(1, server.Db(db => db.Quotations.Count()));
            window.Close();
        }
        finally { QuotationEditorViewModel.SendRetryInterval = oldRetry; }
    }
}
