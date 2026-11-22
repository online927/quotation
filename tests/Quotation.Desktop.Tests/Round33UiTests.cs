using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Controls;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 33: sums in number boxes, Excel pastes with prices, stars, festivals …</summary>
public class Round33UiTests
{
    internal sealed class Ctx : IDisposable
    {
        public required TestServer Server { get; init; }
        public required AppSession Session { get; init; }
        public required ShellViewModel Shell { get; init; }
        public required MainWindow Window { get; init; }
        public void Dispose()
        {
            Window.Close();
            Shell.Dispose();
            Server.Dispose();
        }
    }

    internal static async Task<Ctx> StartAsync(int products = 300, string user = "ravi", Action<TestServer>? setUp = null)
    {
        var server = new TestServer(products: products, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        setUp?.Invoke(server);
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        await server.LoginAsStandardAsync("ravi");
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync(user, user == "admin" ? "admin123" : "ravi-pass");
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
        return new Ctx { Server = server, Session = session, Shell = shell, Window = window };
    }

    internal static async Task<(QuotationEditorViewModel Editor, QuotationEditorView View)> NewQuotationAsync(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        var editor = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        Dispatcher.UIThread.RunJobs();
        return (editor, ctx.Window.FindDescendantOfType<QuotationEditorView>()!);
    }

    private static TextBox Inner(NumericUpDown box) => box.GetVisualDescendants().OfType<TextBox>().First();

    [AvaloniaFact]
    public async Task Sums_typed_in_the_number_boxes_are_worked_out()
    {
        using var ctx = await StartAsync();
        var (editor, view) = await NewQuotationAsync(ctx);
        try
        {
            NumericUpDown Box(string name) => view.FindControl<NumericUpDown>(name)!;
            void Type(string box, string text)
            {
                var t = Inner(Box(box));
                t.Focus();
                t.Text = text;
                Dispatcher.UIThread.RunJobs();
            }
            string? ChipText(string box) => MathBox.Chip(Box(box)) is { IsOpen: true } chip ? ((TextBlock)((Border)chip.Child!).Child!).Text : null;

            // "=12*2" in Quantity: the answer shows under the box, and leaving the box puts it in
            Type("QtyBox", "=12*2");
            Assert.Equal("= 24 · Enter", ChipText("QtyBox"));
            Type("RateBox", "=3159-5%"); // leaving Quantity
            Assert.Equal(24m, editor.EntryQuantity);
            Assert.Null(ChipText("QtyBox"));
            Assert.Equal("= 3,001.05 · Enter", ChipText("RateBox"));
            Type("DiscountBox", "=10/0"); // leaving Rate; a sum that cannot be worked out shows nothing
            Assert.Equal(3001.05m, editor.EntryRate);
            Assert.Null(ChipText("DiscountBox"));
            Type("QtyBox", "7"); // leaving Disc. %: its old value stays; plain numbers work as before
            Assert.Equal(0m, editor.EntryDiscount ?? 0m);
            Type("RateBox", "=1500+10%");
            Assert.Equal(7m, editor.EntryQuantity);
            Inner(Box("QtyBox")).Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1650m, editor.EntryRate);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }
    [AvaloniaFact]
    public async Task Excel_rows_come_in_with_their_quantities_rates_and_discounts()
    {
        using var ctx = await StartAsync();
        var (editor, view) = await NewQuotationAsync(ctx);
        try
        {
            const string rows = "187-901-10\tUniversal Bevel Protractor\t2\t15200\n530-104\tVernier caliper 0-150mm\t5\t2450\t5%\n2046S\tDial indicator\t3\t3800";
            Assert.True(await editor.OpenPastedListAsync(rows));
            await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
            Dispatcher.UIThread.RunJobs();

            // How the columns were read, shown as chips that can be changed
            Assert.True(view.FindControl<StackPanel>("PasteColumnsBar")!.IsVisible);
            Assert.Equal(["A", "B", "C", "D", "E"], editor.PasteColumns.Select(c => c.Letter));
            Assert.Equal(["Part No", "Name", "Qty", "Rate", "Disc %"], editor.PasteColumns.Select(c => PasteColumnViewModel.RoleNames[c.RoleIndex]));
            // The quantities (not 1 any more), the rates and the discount from Excel; Tally's rate under a different one
            Assert.All(editor.PasteRows, r => Assert.True(r.IsFound, r.Query));
            Assert.Equal([2m, 5m, 3m], editor.PasteRows.Select(r => r.Quantity!.Value));
            Assert.Equal([15200m, 2450m, 3800m], editor.PasteRows.Select(r => r.Rate!.Value));
            Assert.Equal([null, 5m, null], editor.PasteRows.Select(r => r.Discount));
            Assert.All(editor.PasteRows, r => Assert.True(r.RateFromPaste));
            var first = editor.PasteRows[0];
            if (first.Selected!.Product.Rate is { } tally && tally > 0 && tally != 15200m)
                Assert.Equal($"Tally {tally:#,##0.00}", first.TallyRateText);

            editor.AddPastedLinesCommand.Execute(null);
            Assert.Equal([15200m, 2450m, 3800m], editor.Lines.Select(l => l.Rate));
            Assert.Equal([0m, 5m, 0m], editor.Lines.Select(l => l.DiscountPercent));
            Assert.Equal([2m, 5m, 3m], editor.Lines.Select(l => l.Quantity));

            // A column read wrongly is changed with its chip: rate "Left out" → the Tally rates
            Assert.True(await editor.OpenPastedListAsync(rows));
            await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
            editor.PasteColumns[3].RoleIndex = 0;
            await UiHelpers.WaitUntil(() => editor.PasteRows.All(r => r.Rate is null) && !editor.PasteStillSearching);
            Assert.All(editor.PasteRows, r => Assert.False(r.RateFromPaste));
            editor.ClosePasteListCommand.Execute(null);
            Assert.Empty(editor.PasteColumns);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }
    [AvaloniaFact]
    public async Task A_column_of_prices_pasted_into_rate_fills_the_lines_with_undo()
    {
        using var ctx = await StartAsync();
        var (editor, view) = await NewQuotationAsync(ctx);
        try
        {
            Assert.True(await editor.OpenPastedListAsync("187-901-10 x 1\n530-104 x 1\n2046S x 1"));
            await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
            editor.AddPastedLinesCommand.Execute(null);
            Assert.Equal(3, editor.Lines.Count);
            var old = editor.Lines.Select(l => l.Rate).ToList();
            var total = editor.GrandTotalText;

            // Prices copied from Excel, pasted into Rate (no line being changed): lines 1 to 3; the extra price is left out
            var rate = Inner(view.FindControl<NumericUpDown>("RateBox")!);
            QuotationEditorView.PasteIntoRate(editor, rate, "15,200.00\r\n21500\r\n4100\r\n999\r\n");
            Assert.Equal([15200m, 21500m, 4100m], editor.Lines.Select(l => l.Rate));
            Assert.NotEqual(total, editor.GrandTotalText);
            var notice = ctx.Shell.Toasts.Last();
            Assert.Equal("3 prices pasted into Rate — lines 1 to 3", notice.Text);
            Assert.Equal("Undo", notice.ActionText);

            // Undo puts the old rates back
            notice.Action!();
            Assert.Equal(old, editor.Lines.Select(l => l.Rate));
            Assert.Equal(total, editor.GrandTotalText);

            // While line 2 is being changed: from line 2 down; the Rate box shows its new rate
            editor.EditLineCommand.Execute(editor.Lines[1]);
            Assert.Equal(2, editor.PastePriceColumn("700\n800\n900"));
            Assert.Equal([old[0], 700m, 800m], editor.Lines.Select(l => l.Rate));
            Assert.Equal(700m, editor.EntryRate);

            // One number is pasted into the box as usual
            editor.CancelEntryCommand.Execute(null);
            rate.Text = "";
            QuotationEditorView.PasteIntoRate(editor, rate, "1234.5");
            Assert.Equal("1234.5", rate.Text);
            Assert.Equal([old[0], 700m, 800m], editor.Lines.Select(l => l.Rate));
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task A_starred_item_comes_first_in_the_product_list_under_its_heading()
    {
        using var ctx = await StartAsync();
        var (editor, view) = await NewQuotationAsync(ctx);
        try
        {
            var box = view.FindControl<SearchPicker>("ProductBox")!;
            Assert.NotNull(box.Star); // the administrator's switch is on at first
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            ctx.Window.KeyTextInput("caliper");
            await UiHelpers.WaitUntil(() => box.IsDropDownOpen && box.Results.Cast<object>().Count() > 3);
            var hits = box.Results.Cast<ProductSearchHitDto>().ToList();
            Assert.All(hits, h => Assert.Null(h.Heading)); // nothing starred yet: no headings
            var star = box.ResultList.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("star"));
            Assert.True(star.IsVisible);

            // The ☆ on the last row: it is starred and comes first, under "★ YOUR STARRED ITEMS"
            var last = hits[^1];
            await box.ToggleStarAsync(box.Star!, last);
            await UiHelpers.WaitUntil(() => box.Results.Cast<ProductSearchHitDto>().FirstOrDefault()?.Product.Id == last.Product.Id);
            var now = box.Results.Cast<ProductSearchHitDto>().ToList();
            Assert.True(now[0].IsStarred);
            Assert.Equal("★ YOUR STARRED ITEMS", now[0].Heading);
            Assert.Equal("OTHER MATCHES", now[1].Heading);
            Assert.Null(now[2].Heading);
            Assert.True(box.IsDropDownOpen);
            Assert.Null(editor.EntryProduct); // starring does not pick it

            // ★ again: unstarred, back in its place
            await box.ToggleStarAsync(box.Star!, now[0]);
            await UiHelpers.WaitUntil(() => box.Results.Cast<ProductSearchHitDto>().First().Product.Id == hits[0].Product.Id);
            Assert.All(box.Results.Cast<ProductSearchHitDto>(), h => Assert.False(h.IsStarred));
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Settings_shows_the_google_drive_backup_and_backs_up_now()
    {
        using var ctx = await StartAsync(user: "admin", setUp: s => s.Drive = new TestSupport.FakeDrive());
        var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel protractor")).First().Product;
        var q = await ctx.Session.Api.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            CustomerId = (await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 1, Rate = product.Rate ?? 15600, TallyRate = product.Rate }],
        });
        await ctx.Session.Api.ApproveQuotationAsync(q.Id);

        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is SettingsViewModel { Drive: not null });
        var settings = (SettingsViewModel)ctx.Shell.CurrentPage!;
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => t.Header as string == "Backup & updates");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Border>("DriveCard")!.IsVisible);
        Assert.True(settings.DriveConnected);
        Assert.Equal("Not yet (1 PDFs to copy)", settings.DriveLastCopyText);
        Assert.True(view.FindControl<CheckBox>("DriveBackupsBox")!.IsChecked);
        Assert.True(view.FindControl<CheckBox>("DrivePdfsBox")!.IsChecked);

        await settings.DriveBackupNowCommand.ExecuteAsync(null);
        Assert.StartsWith("Today ", settings.DriveLastCopyText);
        Assert.EndsWith(" + 1 PDF", settings.DriveLastCopyText);
        Assert.StartsWith("Copied to Google Drive: database ", settings.InfoMessage);
        Assert.False(settings.HasDriveError);
        Assert.Equal(2, ctx.Server.Drive!.Paths().Count);
    }

    [AvaloniaFact]
    public async Task Eid_and_diwali_looks_come_on_by_themselves_and_go_back_to_the_users_theme()
    {
        FestivalLooks.Today = () => new DateOnly(2027, 3, 10); // Eid al-Fitr 2027
        try
        {
            using var ctx = await StartAsync();
            await UiHelpers.WaitUntil(() => ctx.Session.Settings is not null);
            Assert.Equal(FestivalLooks.Eid, ThemeService.Festival);
            Assert.Equal(AppTheme.GlassBlue, ThemeService.Current); // the user's own theme stays chosen
            Assert.Equal(FestivalLooks.Eid, ThemeService.Shown);
            Assert.True(ThemeService.IsDark);
            var page = (LinearGradientBrush)Application.Current!.Resources["PageBrush"]!;
            Assert.Equal(Color.Parse("#021A18"), page.GradientStops[0].Color);
            Assert.NotNull(Application.Current.Resources["SideArt"]);
            var dashboard = Assert.IsType<DashboardViewModel>(ctx.Shell.CurrentPage);
            Assert.Equal("Eid Mubarak!  ·  Today at a glance", dashboard.GlanceTitle);
            Assert.Contains("festive", ctx.Window.FindDescendantOfType<DashboardView>()!.FindControl<TextBlock>("GlanceTitle")!.Classes);
            await Shot(ctx, "festival-eid");

            // The day after the festival days: the user's own theme again
            FestivalLooks.Today = () => new DateOnly(2027, 3, 14);
            ctx.Session.Settings = ctx.Session.Settings;
            Assert.Null(ThemeService.Festival);
            Assert.Equal(AppTheme.GlassBlue, ThemeService.Shown);
            Assert.False(ThemeService.IsDark);

            // Diwali
            FestivalLooks.Today = () => new DateOnly(2027, 10, 29);
            ctx.Session.Settings = ctx.Session.Settings;
            Assert.Equal(FestivalLooks.Diwali, ThemeService.Shown);
            Assert.Equal("Happy Diwali!  ·  Today at a glance", FestivalLooks.GlanceTitle);
            ctx.Shell.Navigate("history");
            ctx.Shell.Navigate("dashboard");
            await Shot(ctx, "festival-diwali");

            // The administrator's switch off: no festival look
            ctx.Session.Settings!.Quotation.FestivalLooks = false;
            ctx.Session.Settings = ctx.Session.Settings;
            Assert.Null(ThemeService.Festival);
            Assert.Equal(AppTheme.GlassBlue, ThemeService.Shown);
        }
        finally
        {
            FestivalLooks.Today = () => DateOnly.FromDateTime(DateTime.Today);
            ThemeService.SetFestival(null);
        }
    }

    private static async Task Shot(Ctx ctx, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
        ctx.Window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public async Task The_top_bar_shows_the_user_as_one_round_initial()
    {
        using var ctx = await StartAsync();
        var shell = ctx.Window.FindDescendantOfType<ShellView>()!;
        var user = shell.FindControl<Button>("UserMenuButton")!;
        Assert.Contains("round", user.Classes);
        Assert.Equal(user.Bounds.Width, user.Bounds.Height, 1); // round: as wide as it is high
        Assert.Equal("R", shell.FindControl<TextBlock>("UserInitialsText")!.Text);
        Assert.Equal("ravi · Standard", ToolTip.GetTip(user));
        // The name is no longer written in the top bar (it is in the menu and when pointing at the initial)
        Assert.DoesNotContain(user.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "ravi" or "Standard");
        Assert.NotNull(user.Flyout);
    }

    [AvaloniaFact]
    public async Task The_connection_is_a_round_button_with_signal_bars()
    {
        using var ctx = await StartAsync();
        var shell = ctx.Window.FindDescendantOfType<ShellView>()!;
        var button = shell.FindControl<Button>("StatusChip")!;
        var bars = shell.FindControl<StackPanel>("SignalBars")!;
        Assert.Contains("round", button.Classes);
        Assert.Equal(button.Bounds.Width, button.Bounds.Height, 1);
        Assert.DoesNotContain(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "Connected" or "Disconnected");
        int Lit() => bars.Children.OfType<Border>().Count(b => b.Opacity > 0.9);

        await UiHelpers.WaitUntil(() => ctx.Shell.Status is not null);
        ctx.Shell.Status = ctx.Shell.Status! with { DataIsStale = false, TallyBusy = false, TallyConnected = true };
        ctx.Shell.ServerReachable = true;
        Dispatcher.UIThread.RunJobs();
        if (ctx.Shell.HealthOk) Assert.Equal(3, Lit()); // connected: three bars (orange two when something is slow or old)

        ctx.Shell.Status = ctx.Shell.Status with { DataIsStale = true };
        Dispatcher.UIThread.RunJobs();
        Assert.True(ctx.Shell.HealthWarn);
        Assert.Contains("warn", bars.Classes);
        Assert.Equal(2, Lit());

        ctx.Shell.Status = ctx.Shell.Status with { TallyConnected = false };
        Dispatcher.UIThread.RunJobs();
        Assert.True(ctx.Shell.HealthBad);
        Assert.Equal(1, Lit());
        Assert.Equal("Disconnected", ctx.Shell.HealthLabel); // shown in the details when clicked
    }

    [AvaloniaFact]
    public async Task Right_alt_p_goes_to_the_packing_and_forwarding_box_too()
    {
        using var ctx = await StartAsync();
        var (editor, view) = await NewQuotationAsync(ctx);
        try
        {
            var packing = Inner(view.FindControl<NumericUpDown>("PackingBox")!);
            foreach (var keys in new[] { RawInputModifiers.Alt, RawInputModifiers.Control | RawInputModifiers.Alt }) // left Alt; right Alt (AltGr)
            {
                view.FindControl<SearchPicker>("ProductBox")!.InputBox.Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.False(packing.IsFocused);
                ctx.Window.KeyPressQwerty(PhysicalKey.P, keys);
                Dispatcher.UIThread.RunJobs();
                Assert.True(packing.IsFocused);
                if (keys == RawInputModifiers.Alt) continue;
                // A letter the AltGr key may still type (AltGr+P is "ö" on some layouts) does not land in the box
                ctx.Window.KeyTextInput("ö");
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("ö", packing.Text ?? "");
            }
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task The_packing_and_forwarding_row_does_not_overlap_without_gst()
    {
        using var ctx = await StartAsync();
        var (editor, view) = await NewQuotationAsync(ctx);
        try
        {
            var p = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
            editor.SelectProduct(p);
            editor.EntryQuantity = 2;
            editor.CommitEntryCommand.Execute(null);
            editor.PackingForwarding = 500;
            foreach (var without in new[] { false, true })
            {
                editor.WithoutGst = without;
                await Task.Delay(200);
                Dispatcher.UIThread.RunJobs();
                Rect At(Control c) => new(c.TranslatePoint(new Point(0, 0), ctx.Window)!.Value, c.Bounds.Size);
                var label = At(view.FindControl<TextBlock>("PackingLabelText")!);
                var box = At(view.FindControl<NumericUpDown>("PackingBox")!);
                var amount = At(view.FindControl<TextBlock>("PackingAmountText")!);
                Assert.True(label.Right <= box.Left, $"label {label} runs into the box {box}");
                Assert.True(box.Right <= amount.Left, $"box {box} runs into the amount {amount}");
                Assert.Equal("Packing & Forwarding ₹", view.FindControl<TextBlock>("PackingLabelText")!.Text);
                Assert.Equal(without, view.FindControl<TextBlock>("PackingNoGstText")!.IsVisible);
            }
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Sharp_text_is_a_switch_in_my_settings_and_off_is_the_old_look()
    {
        using var ctx = await StartAsync();
        var clearTypeHere = SharpTextLook.ClearTypeHere;
        SharpTextLook.ClearTypeHere = true; // the Windows look, on any computer
        try
        {
            ctx.Shell.Navigate("mysettings");
            var mine = Assert.IsType<MySettingsViewModel>(ctx.Shell.CurrentPage);
            Dispatcher.UIThread.RunJobs();
            var view = ctx.Window.FindDescendantOfType<MySettingsView>()!;
            Assert.NotNull(view.FindControl<ToggleSwitch>("SharpTextSwitch"));
            Assert.True(ctx.Session.Config.SharpText); // on at first
            double Small() => (double)Application.Current!.Resources["FontSmall"]!;

            mine.SharpText = false; // the old look
            Assert.False(ctx.Session.Config.SharpText);
            Assert.Equal(SharpTextLook.SmallOff, Small());
            Assert.Equal(TextRenderingMode.Unspecified, RenderOptions.GetTextRenderingMode(ctx.Window));
            Assert.False(ClientConfig.Load(ctx.Session.Config.FilePath).SharpText); // kept on this PC

            mine.SharpText = true;
            Assert.Equal(SharpTextLook.SmallOn, Small());
            Assert.Equal(TextRenderingMode.SubpixelAntialias, RenderOptions.GetTextRenderingMode(ctx.Window));
            Assert.Equal("Sharp text on for this PC.", mine.InfoMessage);

            // A window opened later draws its letters the same way
            var later = new Window();
            later.Show();
            Assert.Equal(TextRenderingMode.SubpixelAntialias, RenderOptions.GetTextRenderingMode(later));
            later.Close();
        }
        finally
        {
            SharpTextLook.ClearTypeHere = clearTypeHere;
            SharpTextLook.Apply(true);
        }
    }

    /// <summary>A quotation made and approved on the New quotation page (where Clear shows).</summary>
    internal static async Task<(QuotationEditorViewModel Editor, QuotationEditorView View)> ApprovedOnNewPageAsync(Ctx ctx)
    {
        var (editor, view) = await NewQuotationAsync(ctx);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.EntryQuantity = 2;
        editor.CommitEntryCommand.Execute(null);
        Assert.False(editor.ShowClear); // a draft: no Clear
        await editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => editor is { IsFinal: true, IsBusy: false });
        Dispatcher.UIThread.RunJobs();
        return (editor, view);
    }

    [AvaloniaFact]
    public async Task Clear_on_an_approved_quotation_starts_a_new_empty_one()
    {
        using var ctx = await StartAsync();
        var (approved, view) = await ApprovedOnNewPageAsync(ctx);
        var id = approved.Id!.Value;

        // Approved on the New quotation page: a solid Clear on the right, where Cancel was
        Assert.True(approved.ShowClear);
        var clear = view.FindControl<Button>("ClearButton")!;
        Assert.True(clear.IsVisible);
        Assert.Contains("primary", clear.Classes);
        Assert.False(view.FindControl<Button>("CancelDocButton")!.IsVisible);

        approved.ClearCommand.Execute(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e != approved && e.Id is null);
        var fresh = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        Assert.Empty(fresh.Lines);
        Assert.False(fresh.ShowClear);

        // Back: the approved one, unchanged; Ctrl+N clears again
        ctx.Shell.GoBack();
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage == approved);
        Assert.Contains((await ctx.Session.Api.QuotationAsync(id)).Status, new[] { QuotationStatus.Approved, QuotationStatus.Generated }); // still approved
        Dispatcher.UIThread.RunJobs();
        ctx.Window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, null);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e != approved && e.Id is null);
        var second = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        second.IsDirty = false;

        // The same approved quotation opened from Today's Quotations or History: no Clear, and Ctrl+N does nothing
        ctx.Shell.OpenQuotation(id);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel { IsFinal: true, IsBusy: false } e && e != approved);
        var opened = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        Dispatcher.UIThread.RunJobs();
        Assert.False(opened.ShowClear);
        Assert.False(ctx.Window.FindDescendantOfType<QuotationEditorView>()!.FindControl<Button>("ClearButton")!.IsVisible);
        ctx.Window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(opened, ctx.Shell.CurrentPage);
    }
}
