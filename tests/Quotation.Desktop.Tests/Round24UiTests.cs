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

/// <summary>Round 24: a list pasted into the Product box; the top search jumps to a quotation by its number.</summary>
public class Round24UiTests
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

    private static async Task<Ctx> SetupAsync(Func<TestServer, QuotationApiClient, Task>? prepare = null)
    {
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        await server.LoginAsStandardAsync("ravi");
        if (prepare is not null) await prepare(server, admin);
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("ravi", "ravi-pass");
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

    private const string WhatsAppList = """
        Dear Sir,
        Please quote for the following:
        1. 187-901-10 x 2
        2. 530-104 - 5 nos
        3. 3 core 2.5 sqmm flexible cable 100
        4. 2046S qty 3
        5. zzqx gizmo
        Thanks & Regards
        """;

    private static async Task<(QuotationEditorViewModel Vm, QuotationEditorView View)> NewQuotationAsync(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e.NumberText.Contains("provisional"));
        var vm = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        await vm.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        vm.FoldHeader();
        Dispatcher.UIThread.RunJobs();
        return (vm, ctx.Window.FindDescendantOfType<QuotationEditorView>()!);
    }

    [AvaloniaFact]
    public async Task Pasted_list_finds_items_asks_when_unsure_and_adds_the_lines()
    {
        using var ctx = await SetupAsync();
        var (vm, view) = await NewQuotationAsync(ctx);
        var box = view.FindControl<Quotation.Desktop.Controls.SearchPicker>("ProductBox")!;
        box.Focus();
        await view.PasteIntoProductAsync(vm, box.InputBox, WhatsAppList);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsPasteOpen);
        Assert.True(view.FindControl<Border>("PasteOverlay")!.IsVisible);
        Assert.Equal(["187-901-10", "530-104", "3 core 2.5 sqmm flexible cable", "2046S", "zzqx gizmo"], vm.PasteRows.Select(r => r.Query));
        Assert.Equal([2m, 5m, 100m, 3m, 1m], vm.PasteRows.Select(r => r.Quantity!.Value));
        Assert.Equal(PasteRowState.Found, vm.PasteRows[0].State);
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", vm.PasteRows[0].ItemName);
        Assert.Equal("530-104-VERNIER CALIPER 0-150MM", vm.PasteRows[1].ItemName);
        Assert.Equal(PasteRowState.Choose, vm.PasteRows[2].State); // Polycab, Polycab FR and Havells all match
        Assert.True(vm.PasteRows[2].Candidates.Count >= 2);
        Assert.Equal("2046S-DIAL INDICATOR 0-10MM", vm.PasteRows[3].ItemName);
        Assert.Equal(PasteRowState.NotFound, vm.PasteRows[4].State);
        Assert.Equal((3, 1, 1), (vm.PasteReadyCount, vm.PasteChooseCount, vm.PasteNotFoundCount));
        Assert.Equal("Add 4 lines", vm.PasteAddText);
        await Shot(ctx.Window, "r24-paste-list");

        // Choosing from the close matches confirms the line; the not-found line is searched by hand.
        var other = vm.PasteRows[2].Candidates.First(c => c != vm.PasteRows[2].Selected);
        vm.PasteRows[2].Selected = other;
        Assert.Equal(PasteRowState.Found, vm.PasteRows[2].State);
        var searchButton = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Search…" && b.IsEffectivelyVisible);
        searchButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.PasteRows[4].ShowsSearchBox);
        var rowSearch = view.GetVisualDescendants().OfType<Quotation.Desktop.Controls.SearchPicker>().Single(p => p.Name == "PasteSearchBox" && p.IsEffectivelyVisible);
        Assert.True(rowSearch.InputBox.IsFocused);
        rowSearch.InputBox.Text = "safety helmet";
        await UiHelpers.WaitUntil(() => rowSearch.IsDropDownOpen && rowSearch.Results.Count > 0);
        await Shot(ctx.Window, "r24-paste-search");
        ctx.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // takes the highlighted item
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("SAFETY HELMET WHITE (RATCHET)", vm.PasteRows[4].ItemName);
        Assert.Equal("Add 5 lines", vm.PasteAddText);

        vm.AddPastedLinesCommand.Execute(null);
        Assert.False(vm.IsPasteOpen);
        Assert.Equal(5, vm.Lines.Count);
        Assert.Equal([1, 2, 3, 4, 5], vm.Lines.Select(l => l.LineNo));
        Assert.Equal(2, vm.Lines[0].Quantity);
        Assert.Equal(15600m, vm.Lines[0].Rate); // the Tally rate, as when typed
        Assert.Equal(other.Product.Name, vm.Lines[2].ItemName);
        Assert.Equal(100, vm.Lines[2].Quantity);
        Assert.True(vm.IsDirty);
        Assert.Contains(ctx.Shell.Toasts, t => t.Text.Contains("5 lines added"));
        await Shot(ctx.Window, "r24-paste-added");

        Assert.True(await vm.SaveAsync());
    }

    [AvaloniaFact]
    public async Task Ctrl_V_with_a_list_opens_the_box_and_one_line_is_typed_in()
    {
        using var ctx = await SetupAsync();
        var (vm, view) = await NewQuotationAsync(ctx);
        var box = view.FindControl<Quotation.Desktop.Controls.SearchPicker>("ProductBox")!;
        var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;

        // One line: an ordinary paste into the box.
        await clipboard.SetTextAsync("bevel protractor\r\n");
        box.InputBox.Focus();
        ctx.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        await UiHelpers.WaitUntil(() => vm.ProductSearchText == "bevel protractor");
        Assert.False(vm.IsPasteOpen);

        // A list: the pasted-list box; Esc closes it without adding anything.
        box.InputBox.Text = "";
        await clipboard.SetTextAsync("187-901-10 x 2\n530-104 - 5 nos");
        ctx.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        await UiHelpers.WaitUntil(() => vm.IsPasteOpen && !vm.PasteStillSearching);
        Assert.Equal(2, vm.PasteRows.Count);
        Assert.Equal("", vm.ProductSearchText ?? "");
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Button>("PasteAddButton")!.IsFocused); // Enter adds them
        ctx.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsPasteOpen);
        Assert.Empty(vm.Lines);
        vm.IsDirty = false;
    }

    [AvaloniaFact]
    public async Task Top_search_opens_a_quotation_by_its_number()
    {
        var ids = new List<Guid>();
        using var ctx = await SetupAsync(async (server, admin) =>
        {
            var ravi = server.CreateApi();
            await ravi.LoginAsync("ravi", "ravi-pass");
            var customer = (await ravi.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
            var product = (await ravi.SearchProductsAsync("universal bevel")).First().Product;
            for (var i = 0; i < 3; i++)
            {
                var q = await ravi.CreateQuotationAsync(new SaveQuotationRequest
                {
                    Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
                    Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = i + 1, Rate = -1 }],
                });
                await ravi.ApproveQuotationAsync(q.Id);
                ids.Add(q.Id);
            }
        });
        var second = await ctx.Session.Api.QuotationAsync(ids[1]);
        var searchBox = ctx.Window.FindDescendantOfType<ShellView>()!.FindControl<TextBox>("GlobalSearchBox")!;
        searchBox.Focus();

        ctx.Shell.GlobalSearch = "2";
        await UiHelpers.WaitUntil(() => ctx.Shell.NumberMatches.Count == 1);
        Assert.True(ctx.Shell.ShowNumberMatches);
        Assert.Equal("Quotation " + second.Number, ctx.Shell.NumberMatches[0].Title);
        await Shot(ctx.Window, "r24-jump-number");

        await ctx.Shell.SearchCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel { Lines.Count: 1 } e && e.Id == ids[1]);
        Assert.Equal("", ctx.Shell.GlobalSearch);
        Assert.Empty(ctx.Shell.NumberMatches);

        // The full number works the same way, typed in any case.
        ctx.Shell.GlobalSearch = second.Number!.ToLowerInvariant();
        await UiHelpers.WaitUntil(() => ctx.Shell.NumberMatches.Count == 1);
        ctx.Shell.GlobalSearch = "";

        // A number no quotation has: Enter searches Quotation History as before.
        ctx.Shell.GlobalSearch = "530104";
        await ctx.Shell.SearchCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationListViewModel);
    }

    [Fact]
    public void Numbers_and_other_searches_are_told_apart()
    {
        Assert.Equal((142, (string?)null), ShellViewModel.NumberQuery("142"));
        Assert.Equal(((int?)null, "TSQ2627-142"), ShellViewModel.NumberQuery(" TSQ2627-142 "));
        Assert.Null(ShellViewModel.NumberQuery("sonepar"));
        Assert.Null(ShellViewModel.NumberQuery("bevel 150"));
        Assert.Null(ShellViewModel.NumberQuery("0"));
        Assert.Null(ShellViewModel.NumberQuery("1234567")); // too long for a number: an ordinary search
    }
}
