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

/// <summary>Round 26: the Formater page (a quotation typed as text, like the Formater app).</summary>
public class Round26UiTests
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

    private const string Text = """
        Sonepar India Private

        R

        187-901-10, 2, 15600
        Make - Mitutoyo

        530-104, 5, 3250, 5

        Digital Caliper 150mm Special, 2, 3450
        Make - Insize
        """;

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

    [AvaloniaFact]
    public async Task Text_is_understood_matched_to_tally_and_approved_as_a_normal_quotation()
    {
        using var ctx = await SetupAsync();
        Assert.Contains(ctx.Shell.NavItems, n => n.Key == "formater");
        Assert.Equal(1, ctx.Shell.NavItems.ToList().FindIndex(n => n.Key == "formater") - ctx.Shell.NavItems.ToList().FindIndex(n => n.Key == "new"));
        var vm = await OpenFormaterAsync(ctx, Text);

        // Two Tally ledgers are called Sonepar India Private Limited: the closest is offered, "Use" takes it.
        Assert.True(vm.CustomerIsTyped);
        Assert.True(vm.HasCustomerSuggestion);
        await Shot(ctx.Window, "r26-customer-suggestion");
        await vm.UseCustomerSuggestionCommand.ExecuteAsync(null);
        Assert.True(vm.CustomerIsTally);
        Assert.Contains("GSTIN", vm.CustomerDetails);
        Assert.Equal(3, vm.Lines.Count);
        Assert.True(vm.Lines[0].IsTally);
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", vm.Lines[0].ItemName);
        Assert.Equal("530-104-VERNIER CALIPER 0-150MM", vm.Lines[1].ItemName);
        Assert.Equal(5m, vm.Lines[1].Typed.DiscountPercent);
        Assert.True(vm.Lines[2].IsTyped); // not in Tally: printed as typed
        Assert.Equal(18m, vm.Lines[2].GstRate);
        Assert.True(vm.CanApprove);
        Assert.Empty(vm.Problems);
        Assert.Equal("R", vm.OtherRefText); // the part after the first blank line
        await Shot(ctx.Window, "r26-formater");
        // Every theme draws it (the shared quotation-page styles).
        foreach (var theme in AppTheme.All.Where(t => t != AppTheme.System))
        {
            ThemeService.Apply(theme);
            Dispatcher.UIThread.RunJobs();
            ctx.Window.CaptureRenderedFrame();
        }
        ThemeService.Apply(AppTheme.Light);

        // Preview: nothing saved, no number used.
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(ctx.Files.Opened, f => f.Contains("Preview-Formater"));
        Assert.Empty(await ctx.Session.Api.QuotationsAsync());

        // Approve: on to the quotation screen, which approves it with the next number.
        vm.ApproveCommand.Execute(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel { Status: QuotationStatus.Generated }, 30000);
        var editor = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        Assert.Equal(QuotationSource.Formater, editor.Source);
        Assert.Equal(3, editor.Lines.Count);
        Assert.Null(editor.Lines[2].ProductId);
        Assert.Equal("Digital Caliper 150mm Special", editor.Lines[2].ItemName);
        await Shot(ctx.Window, "r26-approved");
        var saved = Assert.Single(await ctx.Session.Api.QuotationsAsync());
        Assert.Equal(editor.Number, saved.Number);
        Assert.Equal("Sonepar India Private Limited", (await ctx.Session.Api.QuotationAsync(saved.Id)).Buyer.Name);
    }

    [AvaloniaFact]
    public async Task A_customer_not_in_tally_is_kept_as_typed_and_problems_are_listed()
    {
        using var ctx = await SetupAsync();
        var vm = await OpenFormaterAsync(ctx, """
            BuyersHub
            - Plot 27 , PRT Colony , Karmanghat
            - Hyderabad Telangana 500079
            36AAZFB2304B1ZQ

            Your enquiry dated 10-Nov-2026

            Vernier Hook Depth Gauge, 5, 12011, 10
            Make - Insize

            Dial Gauge, 2, 12O11, 10
            """);
        Assert.True(vm.CustomerIsTyped);
        Assert.Equal("Your enquiry dated 10-Nov-2026", vm.OtherRefText);
        Assert.DoesNotContain("enquiry", vm.CustomerDetails);
        Assert.StartsWith("Plot 27", vm.CustomerDetails); // the "-" marks an address line and is not printed
        Assert.True(vm.CustomerDetails.Contains("Telangana (36)"), vm.CustomerDetails + " | " + vm.GstNote);
        Assert.StartsWith("IGST", vm.GstNote);
        Assert.Single(vm.Lines);
        Assert.NotEmpty(vm.Problems); // "12O11" (a letter O) is not a rate
        Assert.False(vm.CanApprove);
        await Shot(ctx.Window, "r26-typed-customer");

        vm.Text = vm.Text.Replace("Dial Gauge, 2, 12O11, 10", "");
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.CanApprove);
        var q = vm.ToQuotation();
        Assert.Null(q.CustomerId);
        Assert.Equal("36", q.Buyer.StateCode);
        Assert.Equal("Plot 27 , PRT Colony , Karmanghat" + Environment.NewLine + "Hyderabad Telangana 500079", q.Buyer.Address);
        Assert.Equal(QuotationSource.Formater, q.Source);
    }
}
