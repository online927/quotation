using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>The Tally theme: looks like TallyPrime (square, flat, yellow bar, red menu letters, key bar) and asks "Approve? Yes or No".</summary>
public class TallyThemeUiTests
{
    private sealed class Ctx : IDisposable
    {
        public required TestServer Server { get; init; }
        public required AppSession Session { get; init; }
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
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("ravi", "ravi-pass");
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
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

    private static void ClickKey(ShellView shell, string keys)
    {
        var button = shell.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("keyitem") && (b.DataContext is KeyBarItem k ? k.Keys == keys : keys == "Esc" && b.DataContext is not KeyBarItem));
        Assert.True(button.IsEffectivelyEnabled, keys + " is grey");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Color ColourOf(string key) => ((ISolidColorBrush)Application.Current!.Resources[key]!).Color;

    [AvaloniaFact]
    public async Task The_tally_theme_looks_and_works_like_tally()
    {
        using var ctx = await SetupAsync();
        await ctx.Session.SetThemeAsync(AppTheme.Tally);
        Dispatcher.UIThread.RunJobs();
        var shell = ctx.Window.FindDescendantOfType<ShellView>()!;

        // Square, flat, the TallyPrime colours.
        Assert.Contains("tally", shell.Classes);
        Assert.Equal(new CornerRadius(0), Application.Current!.Resources["CardRadius"]);
        Assert.True(ColourThemes.IsFlat);
        Assert.Equal(Color.Parse("#1F4D63"), ColourOf("TopBarBrush"));
        Assert.Equal(Color.Parse("#F9C74F"), ColourOf("SidebarSelectedBrush"));
        Assert.Equal(Color.Parse("#F9C74F"), ColourOf("ListSelectedBrush"));
        Assert.Equal((Color.Parse("#1F4D63"), Colors.White), (ThemeService.TitleBarColours().Caption, ThemeService.TitleBarColours().Text));

        // The side menu like Gateway of Tally: headings in capitals, each item with its own red letter.
        var letters = ctx.Shell.NavItems.Select(n => n.HotKey).Where(c => c != '\0').ToList();
        Assert.Equal(ctx.Shell.NavItems.Count, letters.Count);
        Assert.Equal(letters.Count, letters.Distinct().Count());
        Assert.Equal('D', ctx.Shell.NavItems.First(n => n.Key == "dashboard").HotKey);
        Assert.Contains(shell.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("caps") && t.Text == "DOCUMENTS" && t.IsEffectivelyVisible);
        Assert.Contains(shell.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("navhot") && t.IsEffectivelyVisible);
        Assert.DoesNotContain(shell.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("navtitle") && t.IsEffectivelyVisible);

        // The key bar: the Dashboard's keys black, the quotation keys grey.
        Assert.True(shell.FindControl<Border>("KeyBarPanel")!.IsEffectivelyVisible);
        Assert.True(ctx.Shell.KeyBar.First(k => k.Label == "Refresh").IsOn);
        Assert.False(ctx.Shell.KeyBar.First(k => k.Label == "Save").IsOn);
        await Shot(ctx.Window, "tally-dashboard");

        // On the Dashboard a letter opens its page (Today's Quotations); on other pages letters do nothing.
        var today = ctx.Shell.NavItems.First(n => n.Key == "today");
        TopLevel.GetTopLevel(shell)!.FocusManager!.ClearFocus();
        ctx.Window.KeyPressQwerty(Enum.Parse<PhysicalKey>(today.HotKey.ToString()), RawInputModifiers.None);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationListViewModel);
        ctx.Window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        Assert.True(ctx.Shell.KeyBar.First(k => k.Label == "Print").IsOn);

        // The quotation screen: the document type in a dark box; the key bar's keys work when clicked.
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        if (editor.EntryRate is null or 0) editor.EntryRate = 100;
        editor.CommitEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.Equal(new CornerRadius(0), view.FindControl<Border>("NumberTicket")!.CornerRadius); // a square dark box, like a voucher type
        Assert.True(ctx.Shell.KeyBar.First(k => k.Label == "Save").IsOn);

        ClickKey(shell, "F1");
        Assert.True(ctx.Shell.ShowShortcuts);
        ClickKey(shell, "Esc");
        Assert.False(ctx.Shell.ShowShortcuts);

        ClickKey(shell, "Alt+C"); // the Customer box
        var focused = TopLevel.GetTopLevel(shell)!.FocusManager!.GetFocusedElement() as Visual;
        Assert.True(focused is not null && view.FindControl<Quotation.Desktop.Controls.SearchPicker>("CustomerBox")!.IsVisualAncestorOf(focused));

        ClickKey(shell, "Ctrl+S"); // Save
        await UiHelpers.WaitUntil(() => editor.Id is not null && !editor.IsBusy, 20000);
        await Shot(ctx.Window, "tally-quotation");

        // "Approve? Yes or No": N keeps the draft, Y approves.
        var approving = editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is { Title: "Approve ?", PrimaryText: "Yes", SecondaryText: "No" });
        await Shot(ctx.Window, "tally-approve");
        ctx.Window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        await approving;
        Assert.Equal(QuotationStatus.Draft, editor.Status);
        approving = editor.ApproveCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is { Title: "Approve ?" });
        ctx.Window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        await approving;
        Assert.Equal(QuotationStatus.Generated, editor.Status);

        // Back to Glass (Blue): none of the Tally parts.
        await ctx.Session.SetThemeAsync(AppTheme.GlassBlue);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("tally", shell.Classes);
        Assert.False(shell.FindControl<Border>("KeyBarPanel")!.IsEffectivelyVisible);
        Assert.NotEqual(new CornerRadius(0), view.FindControl<Border>("NumberTicket")!.CornerRadius);
        Assert.False(ColourThemes.IsFlat);
        Assert.NotEqual(new CornerRadius(0), Application.Current!.Resources["CardRadius"]);
        Assert.Contains(shell.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("navtitle") && t.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Menu_letters_are_the_first_free_letter_of_a_word()
    {
        NavItem Item(string title) => new(title.ToLowerInvariant(), title, "", () => null!);
        var items = new[] { Item("Dashboard"), Item("Draft Quotations"), Item("Today's Quotations"), Item("Quotation History"), Item("Deleted"), Item("Tally Connection") };
        NavItem.AssignHotKeys(items);
        Assert.Equal("DQTHEC", new string(items.Select(i => i.HotKey).ToArray()));
        Assert.Equal(("Draft ", "Q", "uotations"), (items[1].TitleBefore, items[1].TitleHot, items[1].TitleAfter));
        Assert.Equal(("D", "e", "leted"), (items[4].TitleBefore, items[4].TitleHot, items[4].TitleAfter));
    }
}
