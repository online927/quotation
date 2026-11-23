using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 36: raise all rates, USD quotation, copy from a Tally invoice, activity page, Settings boxes.</summary>
public class Round36UiTests
{
    [AvaloniaFact]
    public async Task Gear_raises_all_rates_by_a_percent_with_undo()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.True(await editor.OpenPastedListAsync("187-901-10 x 1\n530-104 x 1"));
            await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
            editor.AddPastedLinesCommand.Execute(null);
            editor.PastePriceColumn("15000\n1999.99");
            var total = editor.GrandTotalText;
            Assert.NotNull(view.FindControl<TextBox>("RaiseRatesBox"));

            editor.RaiseRatesText = "4";
            editor.RaiseAllRatesCommand.Execute(null);
            Assert.Equal([15600m, 2079.99m], editor.Lines.Select(l => l.Rate));
            var notice = ctx.Shell.Toasts.Last();
            Assert.Equal("2 rates raised by 4 %.", notice.Text);
            Assert.Equal("Undo", notice.ActionText);
            notice.Action!();
            Assert.Equal([15000m, 1999.99m], editor.Lines.Select(l => l.Rate));
            Assert.Equal(total, editor.GrandTotalText);

            editor.RaiseRatesText = "-10 %";
            editor.RaiseAllRatesCommand.Execute(null);
            Assert.Equal([13500m, 1799.99m], editor.Lines.Select(l => l.Rate));
            Assert.Equal("2 rates lowered by 10 %.", ctx.Shell.Toasts.Last().Text);

            editor.RaiseRatesText = "abc";
            editor.RaiseAllRatesCommand.Execute(null);
            Assert.Equal([13500m, 1799.99m], editor.Lines.Select(l => l.Rate));
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task My_settings_usd_makes_the_next_quotation_in_dollars_then_turns_off()
    {
        using var ctx = await Round33UiTests.StartAsync();
        ctx.Shell.Navigate("mysettings");
        var my = Assert.IsType<MySettingsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => !my.IsBusy);
        my.UsdNext = true;                                   // no rate yet: refused
        await UiHelpers.WaitUntil(() => my.ErrorMessage is not null);
        Assert.False(my.UsdNext);
        my.UsdRateText = "80";
        my.UsdNext = true;
        await UiHelpers.WaitUntil(() => ctx.Session.Api.CurrentUser?.UsdNext == true);
        Assert.Equal(80m, ctx.Session.Api.CurrentUser!.UsdRate);

        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.True(editor.IsUsd);
            Assert.True(editor.WithoutGst);
            Assert.Equal("$ USD · 1 USD = ₹ 80.00 · no GST", editor.UsdChipText);
            Assert.True(view.FindControl<Border>("UsdChip")!.IsVisible);
            var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
            editor.SelectProduct(product);
            Assert.Equal(Math.Round(product.Rate!.Value / 80m, 2), editor.EntryRate);   // Tally's rupees shown in dollars
            editor.CommitEntryCommand.Execute(null);
            Assert.StartsWith("$ ", editor.GrandTotalText);
            Assert.True(await editor.SaveAsync());
            Assert.False(ctx.Session.Api.CurrentUser!.UsdNext);  // next quotation only
            Assert.True((await ctx.Session.Api.QuotationAsync(editor.Id!.Value)).IsUsd);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Items_are_copied_from_a_tally_invoice_at_todays_or_the_invoice_rate()
    {
        using var ctx = await Round33UiTests.StartAsync(setUp: s =>
        {
            var v = s.Tally.Data.AddVoucher("GST Sales", DateOnly.FromDateTime(DateTime.Today).AddDays(-2), "TS/2526/1184",
                "SONEPAR INDIA PRIVATE LIMITED", "187-901-10-UNIVERSAL BEVEL PROTRACTOR", 15000m, quantity: 2);
            v.Lines.Add(new global::Tally.Simulator.SimVoucherLine { Item = "OLD CALIPER 0-200MM", Rate = 3100m, Quantity = 1 });
        });
        var admin = await ctx.Server.LoginAsAdminAsync();
        var all = await admin.SettingsAsync();
        all.Quotation.CopyFromTallyInvoice = true;
        await admin.SaveSettingsAsync(all);

        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.True(editor.ShowCopyInvoice);
            Assert.True(view.FindControl<Button>("CopyInvoiceButton")!.IsVisible);
            await editor.OpenInvoicesAsync();
            Assert.True(editor.IsInvoiceOpen);
            Assert.Equal("TS/2526/1184", editor.Invoices[0].Number);
            editor.SelectedInvoice = editor.Invoices[0];
            Assert.Equal(2, editor.InvoiceItems.Count);
            Assert.True(editor.InvoiceItems[0].IsTicked);
            Assert.False(editor.InvoiceItems[1].IsTicked);      // not in Tally now: skipped
            Assert.True(editor.InvoiceItems[1].NotInTally);
            Assert.Equal("Add 1 item", editor.InvoiceAddText);

            // The invoice's rate
            editor.InvoiceUseInvoiceRate = true;
            editor.AddInvoiceItemsCommand.Execute(null);
            Assert.False(editor.IsInvoiceOpen);
            var line = Assert.Single(editor.Lines);
            Assert.Equal(2m, line.Quantity);
            Assert.Equal(15000m, line.Rate);
            Assert.Equal("1 item added from Tally invoice TS/2526/1184 (1 skipped: not in Tally now).", ctx.Shell.Toasts.Last().Text);

            // Today's rate (from Tally now)
            await editor.OpenInvoicesAsync();
            editor.SelectedInvoice = editor.Invoices[0];
            Assert.True(editor.InvoiceUseTodayRate);
            var today = editor.InvoiceItems[0].TodayRate;
            editor.AddInvoiceItemsCommand.Execute(null);
            Assert.Equal(today, editor.Lines[1].Rate);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Admin_sees_who_did_what_the_summary_with_whatsapp_and_a_months_zip()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        var ravi = await ctx.Server.LoginAsStandardAsync();
        var customer = (await ravi.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await ravi.SearchProductsAsync("universal bevel")).First().Product;
        var q = await ravi.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Buyer = new PartyDto { Name = customer.Name }, ConsigneeSameAsBuyer = true,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 1, Rate = 15600m }],
        });
        await ravi.ApproveQuotationAsync(q.Id);

        // The evening summary: off → nothing; on and due → one notice a day with WhatsApp
        Assert.False(await ctx.Shell.ShowEveningSummaryIfDueAsync());
        var all = await ctx.Session.Api.SettingsAsync();
        all.Quotation.EveningSummary = true;
        all.Quotation.EveningSummaryTime = "00:00";
        await ctx.Session.Api.SaveSettingsAsync(all);
        await ctx.Session.GetSettingsAsync(refresh: true);
        Assert.True(await ctx.Shell.ShowEveningSummaryIfDueAsync());
        var notice = ctx.Shell.Toasts.Last();
        Assert.Contains("📄 1 quotation", notice.Text);
        Assert.Equal("WhatsApp", notice.ActionText);
        notice.Action!();
        Assert.Contains("📄 1 quotation", Assert.Single(((RecordingLauncher)ctx.Session.Documents).WhatsAppTexts));
        Assert.False(await ctx.Shell.ShowEveningSummaryIfDueAsync());   // once a day

        // Diagnostics → Who did what, Summary, PDFs of a month
        ctx.Shell.Navigate("diagnostics");
        var diag = Assert.IsType<DiagnosticsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => diag.Activity.Count > 0);
        Assert.Contains(diag.Activity, a => a.Chip == "Approved" && a.User == "ravi");
        diag.ActivityUser = "admin";
        await UiHelpers.WaitUntil(() => diag.Activity.All(a => a.User == "admin"));
        diag.ShowSummaryCommand.Execute(null);
        await UiHelpers.WaitUntil(() => diag.HasSummary);
        diag.ExportFolder = Path.Combine(ctx.Server.DataDirectory, "docs");
        await diag.DownloadMonthZipCommand.ExecuteAsync(null);
        Assert.True(File.Exists(diag.LastZipPath));
        Assert.EndsWith($"Quotations-{DateTime.Today:yyyy-MM}.zip", diag.LastZipPath);
    }

    [AvaloniaFact]
    public async Task Whatsapp_can_open_in_the_apps_own_window()
    {
        using var ctx = await Round33UiTests.StartAsync();
        ctx.Shell.Navigate("mysettings");
        var my = Assert.IsType<MySettingsViewModel>(ctx.Shell.CurrentPage);
        Assert.True(my.WhatsAppOnApp);
        my.WhatsAppInsideApp = true;
        Assert.Equal(DocumentLauncher.WhatsAppInApp, ctx.Session.Config.WhatsAppOpensIn);
        Assert.False(my.WhatsAppOnApp);
        Assert.False(my.WhatsAppOnWeb);
        Assert.True(ctx.Session.Documents.WhatsAppInsideApp);
        Assert.Equal("PDF copied. WhatsApp is opening in its own window: press Ctrl+V, then Enter.", ctx.Session.Documents.WhatsAppHintNow(true));
        Assert.Equal("--app=\"https://web.whatsapp.com/send?text=x\" --user-data-dir=\"/p\" --no-first-run --no-default-browser-check --window-size=1100,800",
            DocumentLauncher.AppWindowArguments("https://web.whatsapp.com/send?text=x", "/p"));
        my.WhatsAppOnWeb = true;
        Assert.False(my.WhatsAppInsideApp);
        Assert.Equal(DocumentLauncher.WhatsAppWeb, ctx.Session.Config.WhatsAppOpensIn);
    }
}
