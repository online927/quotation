using Avalonia.Controls;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
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

/// <summary>Round 27: Formater line numbers, P&amp;F box, cleaned paste, remembered matches.</summary>
public class Round27UiTests
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
            ThemeService.Apply(AppTheme.Light);
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    private static async Task<Ctx> SetupAsync()
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        var all = await admin.SettingsAsync();
        all.Company.StateName = "Karnataka";
        all.Company.StateCode = "29";
        await admin.SaveSettingsAsync(all);
        await server.LoginAsStandardAsync("ravi");
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var files = new RecordingLauncher(config);
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = files };
        await session.Api.LoginAsync("ravi", "ravi-pass");
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
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

    private static async Task<FormaterViewModel> OpenFormaterAsync(Ctx ctx, string text)
    {
        FormaterViewModel.RefreshDelay = TimeSpan.Zero;
        ctx.Shell.Navigate("formater");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is FormaterViewModel);
        var vm = (FormaterViewModel)ctx.Shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => vm.NumberText.Length > 0);
        vm.Text = text;
        await vm.RefreshAsync(CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        return vm;
    }


    private static FormaterView ViewOf(Ctx ctx)
    {
        Dispatcher.UIThread.RunJobs();
        return ctx.Window.FindDescendantOfType<FormaterView>()!;
    }

    [AvaloniaFact]
    public async Task Line_numbers_mark_the_problem_line_and_go_to_it_and_pnf_has_its_own_box()
    {
        using var ctx = await SetupAsync();
        var vm = await OpenFormaterAsync(ctx, "BuyersHub\n- Plot 27, PRT Colony\n- Hyderabad, Telangana 500079\n\nYour enquiry dated 10-Nov-2026\n\nVernier Hook Depth Gauge, 5, 12011, 10\nMake - Insize\n\nDial Gauge, 2, 12O11, 10");
        var view = ViewOf(ctx);
        var gutter = view.FindControl<Canvas>("LineGutter")!;
        await UiHelpers.WaitUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return gutter.Children.OfType<TextBlock>().Any(t => t.Text == "10");
        });
        Assert.Equal([10], vm.ProblemLines);
        Assert.Contains(gutter.Children.OfType<Rectangle>(), r => true); // the red bar of line 10
        Assert.Single(view.FindControl<Canvas>("LineMarks")!.Children);
        // Line numbers sit at the height of their lines.
        var one = gutter.Children.OfType<TextBlock>().First(t => t.Text == "1");
        var two = gutter.Children.OfType<TextBlock>().First(t => t.Text == "2");
        Assert.True(Canvas.GetTop(two) > Canvas.GetTop(one));
        Assert.InRange(Canvas.GetTop(one), -2, 12);

        // "Go to line 10" puts the cursor on it.
        Assert.True(vm.Problems.Single().HasLine);
        view.GoToLine(10);
        var box = view.FindControl<TextBox>("FormaterTextBox")!;
        Assert.Equal("Dial Gauge, 2, 12O11, 10", box.SelectedText);

        // P&F: its own box (Alt+P goes there); in the totals and on the quotation.
        ctx.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        var packing = view.FindControl<NumericUpDown>("FormaterPackingBox")!;
        Assert.True(packing.GetVisualDescendants().OfType<TextBox>().First().IsFocused);
        vm.Text = vm.Text.Replace("Dial Gauge, 2, 12O11, 10", "");
        await vm.RefreshAsync(CancellationToken.None);
        vm.PackingForwarding = 500;
        await UiHelpers.WaitUntil(() => vm.HasPacking);
        Assert.Equal(500m, vm.Totals!.PackingForwarding);
        Assert.Equal(500m, vm.ToQuotation().PackingForwarding);
        Assert.Equal(54049.50m + 500m, vm.Totals.Subtotal + vm.Totals.PackingForwarding);
        vm.Text += "\nDial Gauge, 2, 12O11, 10";
        await vm.RefreshAsync(CancellationToken.None);
        await UiHelpers.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return view.FindControl<Canvas>("LineMarks")!.Children.Count == 1; });
        await Shot(ctx.Window, "r27-formater-lines");
    }

    [AvaloniaFact]
    public async Task A_pasted_whatsapp_text_is_cleaned_and_undo_brings_it_back()
    {
        using var ctx = await SetupAsync();
        var vm = await OpenFormaterAsync(ctx, "");
        var view = ViewOf(ctx);
        const string pasted = "[11/11, 10:15 am] Ravi: Hi sir\n[11/11, 10:15 am] Ravi: BuyersHub\n- Hyderabad, Telangana\n\n530-104, 5, 3250, 5\nThanks\nRegards";
        view.PasteText(pasted);
        Assert.Equal("BuyersHub\n- Hyderabad, Telangana\n\n530-104, 5, 3250, 5".Replace("\n", Environment.NewLine), vm.Text);
        Assert.True(vm.HasPasteNote);
        Assert.Contains("2 WhatsApp times and names and 3 greeting lines", vm.PasteNote);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Border>("PasteNoteBar")!.IsVisible);
        await vm.RefreshAsync(CancellationToken.None);
        await Shot(ctx.Window, "r27-paste-cleaned");
        vm.UndoPasteCleanCommand.Execute(null);
        Assert.Equal(pasted, vm.Text);
        Assert.False(vm.HasPasteNote);
    }

    [AvaloniaFact]
    public async Task A_chosen_tally_entry_is_remembered_for_the_same_typing_and_can_be_forgotten()
    {
        using var ctx = await SetupAsync();
        var vm = await OpenFormaterAsync(ctx, "Sonepar India Private\n\n187-901-10, 2, 15600");
        Assert.True(vm.HasCustomerSuggestion);
        await vm.UseCustomerSuggestionCommand.ExecuteAsync(null);
        Assert.True(vm.CustomerIsTally);
        var saved = Assert.Single(await ctx.Session.Api.FormaterMatchesAsync());
        Assert.Equal((FormaterMatchDto.Customer, "sonepar india private"), (saved.Kind, saved.Typed));

        // Next time (any PC): the same typing is matched to that ledger at once.
        ctx.Shell.Navigate("dashboard");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is DashboardViewModel);
        var again = await OpenFormaterAsync(ctx, "SONEPAR  india private\n\n187-901-10, 2, 15600");
        await UiHelpers.WaitUntil(() => again.CustomerIsRemembered);
        Assert.True(again.CustomerIsTally);
        Assert.False(again.CanKeepCustomerTyped);
        await Shot(ctx.Window, "r27-remembered");

        await again.ForgetCustomerCommand.ExecuteAsync(null);
        Assert.True(again.CustomerIsTyped);
        Assert.Empty(await ctx.Session.Api.FormaterMatchesAsync());
    }

    private static async Task<QuotationEditorViewModel> NewQuotationAsync(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        if (editor.EntryRate is null or 0) editor.EntryRate = 100;
        editor.CommitEntryCommand.Execute(null);
        return editor;
    }

    [AvaloniaFact]
    public async Task Ctrl_p_prints_straight_to_the_printer_and_alt_p_goes_to_packing()
    {
        using var ctx = await SetupAsync();
        var editor = await NewQuotationAsync(ctx);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;

        // Alt+P: the cursor in Packing & Forwarding.
        view.FindControl<Control>("ProductBox")!.Focus();
        ctx.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<NumericUpDown>("PackingBox")!.GetVisualDescendants().OfType<TextBox>().First().IsFocused);

        // Ctrl+P on a draft: the DRAFT preview to the printer, nothing saved, no question.
        ctx.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        await UiHelpers.WaitUntil(() => ctx.Files.Printed.Count == 1);
        Assert.StartsWith("DRAFT", ctx.Files.Printed[0].Name);
        Assert.Null(editor.Id);
        Assert.Null(ctx.Shell.PendingDialog);
        Assert.Contains(ctx.Shell.Toasts, t => t.Text == "Sent to printer Test Printer.");

        // Approved: its own PDF.
        await editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => editor.Status == QuotationStatus.Generated, 30000);
        await editor.PrintShortcutCommand.ExecuteAsync(null);
        Assert.Equal(2, ctx.Files.Printed.Count);
        Assert.StartsWith(editor.Number!, ctx.Files.Printed[1].Name);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(ctx.Files.Printed[1].Pdf, 0, 4));
    }

    [AvaloniaFact]
    public async Task Ctrl_alt_shift_x_opens_the_xo_game_and_esc_closes_it()
    {
        using var ctx = await SetupAsync();
        ctx.Window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        var game = Assert.IsType<XoGameViewModel>(ctx.Shell.XoGame);
        game.Skill = 1; // plays its best: never loses
        game.PlayCommand.Execute(game.Cells[4]);
        Assert.Equal("X", game.Cells[4].Mark);
        Assert.Equal(1, game.Cells.Count(c => c.Mark == "O"));
        while (!game.IsOver) game.PlayCommand.Execute(game.Cells.First(c => c.Mark.Length == 0));
        Assert.Equal(0, game.YouWins);
        Assert.Equal(1, game.Draws + game.ComputerWins);
        await Shot(ctx.Window, "r27-xo");
        game.NewGameCommand.Execute(null);
        Assert.All(game.Cells, c => Assert.Equal("", c.Mark));
        ctx.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(ctx.Shell.XoGame);
        Assert.IsType<DashboardViewModel>(ctx.Shell.CurrentPage); // Esc closed the game only
    }

    [AvaloniaFact]
    public async Task Greetings_on_sign_in_approval_and_closing_when_the_administrator_switches_them_on()
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        var all = await admin.SettingsAsync();
        Assert.False(all.Quotation.Greetings); // off at first
        all.Quotation.Greetings = true;
        all.Company.StateCode = "29";
        await admin.SaveSettingsAsync(all);
        await server.LoginAsStandardAsync("ravi");
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var files = new RecordingLauncher(config);
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = files };
        await session.Api.LoginAsync("ravi", "ravi-pass");
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        using var ctx = new Ctx { Server = server, Session = session, Files = files, Window = window, Shell = shell };
        await UiHelpers.WaitUntil(() => shell.Greeting is not null);
        Assert.StartsWith("Assalamu Alaikum", shell.Greeting!.English);
        Assert.Equal("السلام عليكم", shell.Greeting.Arabic);
        await Shot(window, "r27-greeting-signin");
        await UiHelpers.WaitUntil(() => shell.Greeting is null, 5000); // goes by itself

        var editor = await NewQuotationAsync(ctx);
        await editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => editor.Status == QuotationStatus.Generated, 30000);
        var toast = Assert.Single(shell.Toasts, t => t.HasTitle);
        Assert.StartsWith("Alhamdulillah", toast.Title);
        Assert.Contains(editor.Number!, toast.Text);
        await Shot(window, "r27-greeting-approved");

        var closing = shell.ConfirmCloseAsync();
        await UiHelpers.WaitUntil(() => shell.PendingDialog is not null);
        shell.AnswerDialogCommand.Execute(true);
        await UiHelpers.WaitUntil(() => shell.Greeting is not null);
        Assert.Equal("JazakAllah Khair", shell.Greeting!.English);
        await Shot(window, "r27-greeting-close");
        Assert.True(await closing);
    }
}
