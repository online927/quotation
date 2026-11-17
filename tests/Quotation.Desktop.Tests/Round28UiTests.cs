using Avalonia;
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

/// <summary>Round 28: the gear next to Items, the Formater layout switch, Ctrl+P on the lists, the date like the number.</summary>
public class Round28UiTests
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


    private static async Task<QuotationEditorViewModel> ThreeLinesAsync(Ctx ctx)
    {
        var editor = await NewQuotationAsync(ctx);
        foreach (var name in new[] { "digital caliper", "outside micrometer" })
        {
            editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync(name)).First().Product);
            if (editor.EntryRate is null or 0) editor.EntryRate = 100;
            editor.CommitEntryCommand.Execute(null);
        }
        Assert.Equal(3, editor.Lines.Count);
        return editor;
    }

    [AvaloniaFact]
    public async Task The_gear_goes_down_one_column_or_line_by_line_after_a_line_is_changed()
    {
        using var ctx = await SetupAsync();
        var editor = await ThreeLinesAsync(ctx);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.False(editor.StepModeOn); // off at first

        // Off: saving a changed line goes back to a new line, as before.
        editor.EditLineCommand.Execute(editor.Lines[0]);
        editor.SaveLineCommand.Execute(null);
        Assert.Null(editor.EditingLine);

        // Rate: line 1's rate changed, Enter → line 2 opens with the cursor in Rate, and so on to the last line.
        editor.ChooseStepModeCommand.Execute("Rate");
        Assert.Equal("Rate", ctx.Session.Config.LineStepMode); // remembered on this PC
        editor.EditLineCommand.Execute(editor.Lines[0]);
        editor.EntryRate = 14900;
        editor.SaveLineCommand.Execute(null);
        Assert.Equal(14900m, editor.Lines[0].Rate);
        Assert.Same(editor.Lines[1], editor.EditingLine);
        Assert.Equal("Going down: Rate · line 2 of 3", editor.StepInfo);
        await UiHelpers.WaitUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return view.FindControl<NumericUpDown>("RateBox")!.GetVisualDescendants().OfType<TextBox>().First().IsFocused;
        });
        await Shot(ctx.Window, "r28-gear-rate");
        // Enter in the Rate box saves and opens line 3.
        ctx.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(editor.Lines[2], editor.EditingLine);
        editor.SaveLineCommand.Execute(null);
        Assert.Null(editor.EditingLine); // after the last line: a new line

        // The whole line: the next line opens with the cursor in Quantity.
        editor.ChooseStepModeCommand.Execute("Line");
        editor.EditLineCommand.Execute(editor.Lines[0]);
        editor.SaveLineCommand.Execute(null);
        Assert.Same(editor.Lines[1], editor.EditingLine);
        await UiHelpers.WaitUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return view.FindControl<NumericUpDown>("QtyBox")!.GetVisualDescendants().OfType<TextBox>().First().IsFocused;
        });
        editor.ChooseStepModeCommand.Execute("Off");
    }

    [AvaloniaFact]
    public async Task The_date_reads_like_the_number_when_it_cannot_be_changed()
    {
        using var ctx = await SetupAsync();
        await NewQuotationAsync(ctx);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.False(view.FindControl<CalendarDatePicker>("DateBox")!.IsVisible);
        var date = view.FindControl<TextBlock>("DateText")!;
        Assert.True(date.IsVisible);
        Assert.Equal(DateTime.Today.ToString("dd-MMM-yyyy"), date.Text);
    }

    [AvaloniaFact]
    public async Task Ctrl_p_on_the_list_prints_the_clicked_quotation()
    {
        using var ctx = await SetupAsync();
        var editor = await NewQuotationAsync(ctx);
        await editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => editor.Status == QuotationStatus.Generated, 30000);
        ctx.Shell.Navigate("history");
        var list = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => list.Rows.Count == 1);
        await list.PrintSelectedCommand.ExecuteAsync(null);
        Assert.Equal("Click a quotation first.", list.InfoMessage);
        Assert.Empty(ctx.Files.Printed);
        list.Selected = list.Rows[0];
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        await UiHelpers.WaitUntil(() => ctx.Files.Printed.Count == 1);
        Assert.StartsWith(editor.Number!, ctx.Files.Printed[0].Name);
    }

    [AvaloniaFact]
    public async Task The_formater_page_prints_in_the_layout_chosen_on_it()
    {
        using var ctx = await SetupAsync();
        FormaterViewModel.RefreshDelay = TimeSpan.Zero;
        ctx.Shell.Navigate("formater");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is FormaterViewModel);
        var vm = (FormaterViewModel)ctx.Shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => vm.NumberText.Length > 0);
        Assert.True(vm.IsTallyLayout); // the user's own layout at first (Tally)
        vm.Text = "BuyersHub\n- Hyderabad, Telangana\n\n530-104, 5, 3250, 5";
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(PdfLayouts.Tally, vm.ToQuotation().PdfLayout);
        vm.SetLayoutCommand.Execute(PdfLayouts.Zoho);
        Assert.True(vm.IsZohoLayout);
        Assert.Equal(PdfLayouts.Zoho, vm.ToQuotation().PdfLayout);
        await Shot(ctx.Window, "r28-formater-layout");
        vm.SetLayoutCommand.Execute(PdfLayouts.Tally);
    }

    [AvaloniaFact]
    public async Task The_gear_menu_is_used_with_the_mouse_and_enter_goes_down_the_column()
    {
        using var ctx = await SetupAsync();
        var editor = await ThreeLinesAsync(ctx);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var gear = view.FindControl<Button>("StepGearButton")!;
        gear.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var at = gear.TranslatePoint(new Point(15, 15), ctx.Window)!.Value;
        ctx.Window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        ctx.Window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        await UiHelpers.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return gear.Flyout is Avalonia.Controls.Primitives.FlyoutBase { IsOpen: true }; });
        var overlay = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(ctx.Window)!;
        var rate = overlay.GetVisualDescendants().OfType<RadioButton>().Single(r => (r.Content as string) == "Rate");
        var rp = rate.TranslatePoint(new Point(8, 8), ctx.Window)!.Value;
        ctx.Window.MouseDown(rp, Avalonia.Input.MouseButton.Left);
        ctx.Window.MouseUp(rp, Avalonia.Input.MouseButton.Left);
        await UiHelpers.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return gear.Flyout is Avalonia.Controls.Primitives.FlyoutBase { IsOpen: false }; });
        Assert.Equal("Rate", editor.StepMode); // the click's choice is kept (the menu closed after it)

        // Edit on line 1: the cursor goes straight to Rate; Enter there saves and opens line 2 in Rate.
        editor.EditLineCommand.Execute(editor.Lines[0]);
        TextBox RateInput() => view.FindControl<NumericUpDown>("RateBox")!.GetVisualDescendants().OfType<TextBox>().First();
        await UiHelpers.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return RateInput().IsFocused; });
        ctx.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await UiHelpers.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return editor.EditingLine == editor.Lines[1] && RateInput().IsFocused; });
        editor.ChooseStepModeCommand.Execute("Off");
    }
}
