using Avalonia;
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

/// <summary>Round 31: the quotation number as the heading, the date lined up with the References box.</summary>
public class Round31UiTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.GlassBlue)]
    [InlineData(AppTheme.Tally)]
    public async Task The_number_is_in_a_ticket_with_a_status_chip_and_the_date_in_a_pill(string theme)
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        using var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            ThemeService.Apply(theme);
            shell.Navigate("new");
            var editor = Assert.IsType<QuotationEditorViewModel>(shell.CurrentPage);
            await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
            Dispatcher.UIThread.RunJobs();
            var view = window.FindDescendantOfType<QuotationEditorView>()!;

            // The heading is the number (not "New Quotation"), marked provisional until it is saved.
            var number = editor.NumberText.Replace(" (provisional)", "");
            Assert.Equal(number, view.FindControl<TextBlock>("HeadingText")!.Text);
            Assert.Equal("(provisional)", editor.HeadingNote);
            Assert.True(view.FindControl<TextBlock>("HeadingNote")!.IsVisible);
            Assert.Equal("New Quotation", editor.Title); // the back button still names the page

            // The number in its ticket ("QUOTATION NO." over it; the theme's colour, a square dark box in Tally), the
            // status as a chip with a dot, the date in a pill on the right: its right edge on the boxes' right edge, and
            // no word "Dated". The administrator can still change the date (a date box in the pill).
            Assert.Equal("QUOTATION NO.", view.FindControl<TextBlock>("TicketLabel")!.Text);
            Assert.Equal(theme == AppTheme.Tally ? new CornerRadius(0) : new CornerRadius(14), view.FindControl<Border>("NumberTicket")!.CornerRadius);
            Assert.Equal("Draft", editor.StatusChipText);
            Assert.True(view.FindControl<Border>("StatusChip")!.IsVisible);
            Assert.True(view.FindControl<CalendarDatePicker>("DateBox")!.IsVisible);
            Rect InWindow(Control c) => new(c.TranslatePoint(new Point(0, 0), window)!.Value, c.Bounds.Size);
            var references = InWindow(view.FindControl<TextBox>("RefDateBox")!).Right + 14;
            Assert.InRange(InWindow(view.FindControl<Border>("DatePill")!).Right, references - 2, references + 1);
            Assert.DoesNotContain(view.FindControl<Grid>("TitleBar")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Dated");

            // Saved: the number without the note.
            await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
            editor.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
            editor.EntryRate = 100;
            editor.CommitEntryCommand.Execute(null);
            Assert.True(await editor.SaveAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(editor.Number, view.FindControl<TextBlock>("HeadingText")!.Text);
            Assert.False(view.FindControl<TextBlock>("HeadingNote")!.IsVisible);
        }
        finally
        {
            ThemeService.Apply(AppTheme.GlassBlue);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_normal_user_clicking_sync_changes_brings_a_new_tally_rate()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true); // the first sync (normally at installation)
        await server.LoginAsStandardAsync("ravi");
        var session = new AppSession(new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") }, _ => server.CreateApi());
        await session.Api.LoginAsync("ravi", "ravi-pass");
        Assert.False(session.IsAdmin);
        var vm = new TallyViewModel(session);
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = vm }, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            await UiHelpers.WaitUntil(() => vm.Status is not null && vm.Runs.Count > 0);
            // The help line tells the real timing (Settings → Tally: every 180 minutes, nightly at 02:00).
            Assert.Equal("Changes are synchronized automatically every 3 hours and a full sync runs every night at 02:00.", vm.AutoSyncText);
            Assert.Equal("Changes are synchronized automatically every hour.", TallyViewModel.AutoSyncTextFor(60, ""));
            Assert.Equal("Changes are synchronized automatically every 15 minutes and a full sync runs every night at 23:30.",
                TallyViewModel.AutoSyncTextFor(15, "23:30"));

            // A rate is changed in Tally; the user (not the administrator) clicks Sync changes.
            const string item = "187-901-10-UNIVERSAL BEVEL PROTRACTOR";
            server.Tally.ChangeRate(item, 17250);
            await vm.IncrementalSyncCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            Assert.Equal("Succeeded", vm.Runs[0].Status);
            Assert.Equal("Incremental", vm.Runs[0].Kind); // only what changed
            var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;
            Assert.Equal(17250m, product.Rate);

            // Nothing changed since: a quick check only.
            await vm.IncrementalSyncCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage);
            Assert.Contains("No changes in Tally masters", vm.Runs[0].Message);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Whatsapp_links_for_the_app_and_for_whatsapp_web()
    {
        Assert.Equal("whatsapp://send?phone=919822012345&text=Quotation%20TSQ2627-1", DocumentLauncher.WhatsAppUrl(false, "919822012345", "Quotation TSQ2627-1"));
        Assert.Equal("https://web.whatsapp.com/send?phone=919822012345&text=Quotation%20TSQ2627-1", DocumentLauncher.WhatsAppUrl(true, "919822012345", "Quotation TSQ2627-1"));
        Assert.Equal("https://web.whatsapp.com/send?text=Hi%20%26%20thanks", DocumentLauncher.WhatsAppUrl(true, null, "Hi & thanks"));
    }

    [AvaloniaFact]
    public async Task Whatsapp_web_is_chosen_in_my_settings_and_used_by_the_whatsapp_button()
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi());
        var files = new RecordingLauncher(config);
        session.Documents = files;
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var all = await session.Api.SettingsAsync();
        all.Quotation.WhatsAppButton = true;
        all.Quotation.WhatsAppMessage = "Quotation {number}";
        await session.Api.SaveSettingsAsync(all);
        var customer = (await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;
        var saved = await session.Api.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 1, Rate = -1 }],
        });
        await session.Api.ApproveQuotationAsync(saved.Id, overrideStaleData: true);
        using var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel && session.Settings is not null);
            Assert.Equal(DocumentLauncher.WhatsAppApp, config.WhatsAppOpensIn); // the WhatsApp app at first, as before

            // My Settings → WhatsApp (this PC) → WhatsApp Web.
            shell.Navigate("mysettings");
            var mine = Assert.IsType<MySettingsViewModel>(shell.CurrentPage);
            Dispatcher.UIThread.RunJobs();
            var view = window.FindDescendantOfType<MySettingsView>()!;
            Assert.True(view.FindControl<RadioButton>("WhatsAppAppRadio")!.IsChecked);
            view.FindControl<RadioButton>("WhatsAppWebRadio")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(mine.WhatsAppOnWeb);
            Assert.False(view.FindControl<RadioButton>("WhatsAppAppRadio")!.IsChecked);
            Assert.Equal(DocumentLauncher.WhatsAppWeb, ClientConfig.Load(config.FilePath).WhatsAppOpensIn); // remembered on this PC
            Assert.True(files.WhatsAppOnWeb);

            // The WhatsApp button on the quotation: the PDF and the customer's number go to WhatsApp Web.
            shell.OpenQuotation(saved.Id);
            await UiHelpers.WaitUntil(() => shell.CurrentPage is QuotationEditorViewModel { ShowWhatsApp: true });
            var editor = (QuotationEditorViewModel)shell.CurrentPage!;
            await editor.WhatsAppCommand.ExecuteAsync(null);
            var share = Assert.Single(files.WhatsApp);
            Assert.Equal("919822012345", share.Phone);
            Assert.Equal("PDF copied. WhatsApp Web is opening in your browser: press Ctrl+V, then Enter.", editor.InfoMessage);

            // Back to the app.
            mine.WhatsAppOnApp = true;
            Assert.False(mine.WhatsAppOnWeb);
            Assert.Equal(DocumentLauncher.WhatsAppApp, config.WhatsAppOpensIn);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Grass_green_has_its_grass()
    {
        Assert.Equal("Grass Green", ThemeNames.Of(AppTheme.GlassGreen));
        var grass = Quotation.Desktop.Controls.ThemeScenery.For(AppTheme.GlassGreen).ToDictionary(p => p.Key, p => p.Geometry);
        Assert.Equal(new Avalonia.Rect(0, 0, 1000, 200), grass["NearArt"].Bounds); // grass blades stay inside their drawing
        Assert.Equal(new Avalonia.Rect(0, 0, 262, 120), grass["SideArt"].Bounds);
    }

    [AvaloniaFact]
    public async Task The_seal_pin_question_shows_a_rubber_stamp_and_other_questions_the_question_mark()
    {
        using var server = new TestServer(products: 20, customers: 10, today: DateOnly.FromDateTime(DateTime.Today));
        await server.LoginAsStandardAsync("ravi");
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("ravi", "ravi-pass");
        using var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1000, Height = 640 };
        window.Show();
        try
        {
            await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
            var stamp = window.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "SealStampIcon");

            var asking = shell.AskAsync("Your seal", "Enter your 4-digit seal PIN to seal TSQ2627-1.", "Seal PIN", secret: true, ok: "Seal & Save", stamp: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(stamp.IsEffectivelyVisible);
            var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                foreach (var theme in new[] { AppTheme.GlassBlue, AppTheme.Tally, AppTheme.Midnight })
                {
                    ThemeService.Apply(theme);
                    await Task.Delay(300);
                    Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame()?.Save(Path.Combine(dir, "seal-stamp-" + theme + ".png"));
                }
            }
            shell.AnswerDialogCommand.Execute(false);
            Assert.Null(await asking);

            var confirming = shell.ConfirmAsync("Delete?", "Move it to Deleted?");
            Dispatcher.UIThread.RunJobs();
            Assert.False(stamp.IsEffectivelyVisible); // other questions keep the question mark
            shell.AnswerDialogCommand.Execute(false);
            Assert.False(await confirming);
        }
        finally
        {
            ThemeService.Apply(AppTheme.GlassBlue);
            window.Close();
        }
    }
    [AvaloniaFact]
    public async Task Hide_menu_glides_and_is_instant_with_reduce_animations()
    {
        using var server = new TestServer(products: 20, customers: 10, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        using var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
            ThemeService.ApplyDisplay(null, null, reduceMotion: false);
            var bar = window.FindDescendantOfType<ShellView>()!.FindControl<Border>("Sidebar")!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(262, bar.Bounds.Width);

            // Animations on: the width glides (0.22 s) and the names are cut off at the menu's edge while it grows;
            // the menu rows keep to the left, so the icons do not shift while the menu is narrower.
            var glide = Assert.IsType<Avalonia.Animation.DoubleTransition>(Assert.Single(bar.Transitions!));
            Assert.Equal(Avalonia.Layout.Layoutable.WidthProperty, glide.Property);
            Assert.Equal(TimeSpan.FromSeconds(0.22), glide.Duration);
            Assert.True(bar.ClipToBounds);
            Assert.All(bar.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("navgrid") && g.ColumnDefinitions.Count == 3),
                g => Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, g.HorizontalAlignment));
            shell.ToggleSidebarCommand.Execute(null);
            await UiHelpers.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return bar.Bounds.Width == 64; });

            // Reduce animations (My Settings): no glide, the menu changes at once
            ThemeService.ApplyDisplay(null, null, reduceMotion: true);
            Assert.True(bar.Transitions is null || bar.Transitions.Count == 0);
            shell.ToggleSidebarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(262, bar.Bounds.Width);
        }
        finally
        {
            ThemeService.ApplyDisplay(null, null, reduceMotion: false);
            window.Close();
        }
    }
    [AvaloniaFact]
    public async Task Selected_text_stays_readable_and_its_look_is_chosen_in_my_settings()
    {
        using var server = new TestServer(products: 20, customers: 10, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        using var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        static string? Hex(Avalonia.Media.IBrush? b) => b is Avalonia.Media.ISolidColorBrush s ? $"#{s.Color.ToUInt32():x8}" : null;
        static string? Back(TextBox b) => Hex(b.SelectionBrush);
        static string? Letters(TextBox b) => Hex(b.SelectionForegroundBrush);
        try
        {
            await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel);
            TextSelectionLook.Apply(config.TextSelection);
            ThemeService.Apply(AppTheme.GlassBlue);
            shell.Navigate("mysettings");
            var vm = Assert.IsType<MySettingsViewModel>(shell.CurrentPage);
            await UiHelpers.WaitUntil(() => !vm.IsBusy);
            Dispatcher.UIThread.RunJobs();
            var view = window.FindDescendantOfType<MySettingsView>()!;
            var box = window.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible);

            // At first: soft blue with dark letters (the old solid blue with dark letters hid them)
            Assert.Equal(TextSelectionLook.SoftBlue, config.TextSelection);
            Assert.True(view.FindControl<RadioButton>("SelectionSoftBlueRadio")!.IsChecked);
            Assert.Equal("#ffb3d7ff", Back(box));
            Assert.Equal("#ff111827", Letters(box));

            // Blue sketch (white letters), yellow highlighter: saved for this PC, every box follows at once
            view.FindControl<RadioButton>("SelectionSolidBlueRadio")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(("#ff1a73e8", "#ffffffff"), (Back(box), Letters(box)));
            vm.SelectionMarker = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(("#ffffe58a", "#ff111827"), (Back(box), Letters(box)));
            Assert.Equal(TextSelectionLook.Marker, ClientConfig.Load(config.FilePath).TextSelection);
            Assert.Contains("Yellow highlighter", vm.InfoMessage);
            Assert.Equal(["Soft blue", "Blue sketch", "Yellow highlighter", "Theme colour"],
                view.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == "TextSelection").Select(r => r.Content as string));

            // Theme colour only tints: the letters keep their own colour, and it follows the theme
            vm.SelectionThemeColour = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(box.SelectionForegroundBrush);
            Assert.Equal($"#55{TextSelectionLook.Accent().ToUInt32() & 0xFFFFFF:x6}", Back(box));
            ThemeService.Apply(AppTheme.Midnight);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("#55f2c14e", Back(box)); // Midnight Stars' gold
            Assert.True(view.FindControl<RadioButton>("SelectionThemeColourRadio")!.IsChecked);

            // A choice that no longer exists (see-through) becomes soft blue
            Assert.Equal(TextSelectionLook.SoftBlue, TextSelectionLook.Normalize("SeeThrough"));
        }
        finally
        {
            TextSelectionLook.Apply(TextSelectionLook.SoftBlue);
            ThemeService.Apply(AppTheme.GlassBlue);
            window.Close();
        }
    }
}
