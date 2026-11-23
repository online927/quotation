using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 36: Drive folder name, AI company words, month ZIP, activity, USD, Tally invoices, datasheets, summary.</summary>
public class Round36Tests : QuotationTestBase
{
    [Fact]
    public async Task Drive_copies_go_to_the_folder_named_in_settings()
    {
        Assert.Equal("TS Quotations", (await Api.SettingsAsync()).Backup.DriveFolder);
        Server.Drive = new TestSupport.FakeDrive();
        var all = await Api.SettingsAsync();
        all.Backup.DriveFolder = "  Office / Quotations  ";
        await Api.SaveSettingsAsync(all);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);

        var status = await Api.DriveBackupNowAsync();
        Assert.Null(status.Error);
        Assert.All(Server.Drive.Paths(), p => Assert.StartsWith("Office Quotations/", p));
        Assert.Equal("TS Quotations", BackupSettings.DriveFolderName("   "));
    }

    [Fact]
    public void Photo_reader_uses_the_company_name_and_what_we_sell()
    {
        var prompt = Services.AiService.PhotoPromptFor("ACME TRADERS", "tools and instruments.");
        Assert.Contains("enquiries for ACME TRADERS, a company in India selling tools and instruments: a", prompt);
        Assert.DoesNotContain("SAIFUDDIN", prompt);
        Assert.Contains("for a trading company in India: a", Services.AiService.PhotoPromptFor("", ""));
    }

    [Fact]
    public async Task A_months_approved_pdfs_come_as_one_zip()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);
        var draft = await Api.CreateQuotationAsync(await BevelRequest());   // a draft has no PDF: not in the ZIP

        var (zip, name) = await Api.MonthPdfsAsync(Today.Year, Today.Month, quotations: true, proforma: true);
        Assert.Equal($"Quotations-{Today:yyyy-MM}.zip", name);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        var entry = Assert.Single(archive.Entries);
        Assert.StartsWith(approved.Number + " SONEPAR", entry.Name, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", entry.Name);

        // Only proforma invoices: there are none that month
        var none = await Assert.ThrowsAsync<ApiException>(() => Api.MonthPdfsAsync(Today.Year, Today.Month, quotations: false, proforma: true));
        Assert.Contains("No approved PDFs", none.Message);
        Assert.NotNull(draft);
    }

    [Fact]
    public async Task Who_did_what_lists_the_days_work_with_chips()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);

        var today = await Api.ActivityAsync(DateOnly.FromDateTime(DateTime.Now));
        var row = Assert.Single(today, a => a.Chip == "Approved");
        Assert.Equal("ok", row.Kind);
        Assert.StartsWith(approved.Number + " · SONEPAR INDIA PRIVATE LIMITED · ₹ ", row.What, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(today, a => a.Chip == "Created");
        Assert.DoesNotContain(today, a => a.Chip == "Login");

        Assert.Empty(await Api.ActivityAsync(DateOnly.FromDateTime(DateTime.Now), "nobody"));
        Assert.Empty(await Api.ActivityAsync(DateOnly.FromDateTime(DateTime.Now).AddDays(-3)));
    }

    private static string PdfWords(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public async Task A_usd_quotation_is_in_dollars_without_gst_and_the_switch_turns_off()
    {
        var me = await Api.SetMyPreferencesAsync(new MyPreferencesDto(UsdNext: true, UsdRate: 80m));
        Assert.True(me.UsdNext);
        Assert.Equal(80m, me.UsdRate);

        var request = await BevelRequest();
        request.Currency = Currencies.Usd;
        request.ExchangeRate = 80m;
        request.Lines[0].Rate = 195m;                    // dollars
        var q = await Api.CreateQuotationAsync(request);
        Assert.True(q.IsUsd);
        Assert.Equal(80m, q.ExchangeRate);
        Assert.True(q.WithoutGst);
        Assert.Empty(q.Taxes);
        Assert.Equal(390m, q.GrandTotal);
        Assert.Equal("USD Three Hundred Ninety Only", q.AmountInWords);
        Assert.False((await Api.MeAsync()).UsdNext);     // next quotation only
        Assert.Equal(80m, (await Api.MeAsync()).UsdRate);  // the rate is remembered

        await Api.ApproveQuotationAsync(q.Id);
        var words = PdfWords(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("$ 390.00", words);
        Assert.Contains("Amounts in US dollars (USD), without GST. 1 USD =", words);
        Assert.Contains("80.00.", words);
        Assert.Contains("USD Three Hundred Ninety Only", words);

        // A copy is in rupees again
        var copy = await Api.DuplicateQuotationAsync(q.Id);
        Assert.False(copy.IsUsd);
        Assert.Null(copy.ExchangeRate);

        // No rate typed: refused
        request.ExchangeRate = null;
        await Assert.ThrowsAsync<ApiException>(() => Api.CreateQuotationAsync(request));
        Assert.Equal("USD One Thousand Two Hundred Thirty Four and One Cent Only", Quotation.Core.Calculation.IndianFormat.DollarsInWords(1234.01m));
        Assert.Equal("USD One Million Two Hundred Thousand Only", Quotation.Core.Calculation.IndianFormat.DollarsInWords(1_200_000m));
    }

    [Fact]
    public async Task A_customers_tally_invoices_are_read_only_when_switched_on()
    {
        var sonepar = (await Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        // Off at first
        Assert.Contains("switched off", (await Assert.ThrowsAsync<ApiException>(() => Api.TallyInvoicesAsync(sonepar.Id))).Message);

        var all = await Api.SettingsAsync();
        all.Quotation.CopyFromTallyInvoice = true;
        await Api.SaveSettingsAsync(all);
        var v = Server.Tally.Data.AddVoucher("GST Sales", Today.AddDays(-2), "TS/2526/1184", "SONEPAR INDIA PRIVATE LIMITED",
            "187-901-10-UNIVERSAL BEVEL PROTRACTOR", 15000m, quantity: 2);
        v.Lines.Add(new global::Tally.Simulator.SimVoucherLine { Item = "OLD CALIPER 0-200MM", Rate = 3100m, Quantity = 1 });
        Server.Tally.Data.AddVoucher("GST Sales", Today.AddDays(-1), "TS/2526/1190", "BHARAT PRECISION ENGINEERING", "-Rsk Spirit Level 200Mm", 9000m);
        Server.Tally.Data.AddVoucher("GST Sales", Today.AddDays(-400), "TS/2425/0001", "SONEPAR INDIA PRIVATE LIMITED", "-Rsk Spirit Level 200Mm", 8000m);
        var before = Server.Tally.VoucherScanCount;

        var invoices = await Api.TallyInvoicesAsync(sonepar.Id);
        Assert.Equal(["TS/2526/1184", "INV-2041"], invoices.Select(i => i.Number)); // newest first; only Sonepar's sales of 12 months
        var first = invoices[0];
        Assert.Equal(33100m, first.Total);
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(2m, first.Lines[0].Quantity);
        Assert.Equal(15000m, first.Lines[0].InvoiceRate);
        Assert.NotNull(first.Lines[0].Product);                      // still in Tally: today's rate comes with it
        Assert.Null(first.Lines[1].Product);                         // not in Tally now
        Assert.True(Server.Tally.VoucherScanCount - before < 20); // one request through the Sales index
    }

    private static int Pages(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return doc.NumberOfPages;
    }

    [Fact]
    public async Task An_items_datasheet_is_added_after_the_quotation_pages()
    {
        var request = await BevelRequest();
        var productId = request.Lines[0].ProductId!.Value;
        Assert.Null(await Api.DatasheetAsync(productId));
        // Not a PDF: refused
        await Assert.ThrowsAsync<ApiException>(() => Api.UploadDatasheetAsync(productId, "x.pdf", "hello"u8.ToArray()));

        // Any one-page PDF does as a datasheet
        var draft = await Api.CreateQuotationAsync(request);
        var onePage = await Api.PreviewPdfAsync(draft.Id);
        Assert.Equal(1, Pages(onePage));
        var sheet = await Api.UploadDatasheetAsync(productId, "Mitutoyo-187-901-datasheet.pdf", onePage);
        Assert.Equal("Mitutoyo-187-901-datasheet.pdf", sheet.FileName);
        Assert.Equal(1, sheet.Pages);
        Assert.True(sheet.AddToPdf);
        Assert.Equal(onePage.Length, (await Api.DatasheetFileAsync(productId)).Length);

        var q = await Api.CreateQuotationAsync(request);
        await Api.ApproveQuotationAsync(q.Id);
        Assert.Equal(2, Pages(await Api.QuotationPdfAsync(q.Id)));     // the quotation, then the datasheet

        await Api.SetDatasheetOnPdfAsync(productId, false);
        Assert.Equal(1, Pages(await Api.QuotationPdfAsync(q.Id)));
        await Api.SetDatasheetOnPdfAsync(productId, true);
        await Api.RemoveDatasheetAsync(productId);
        Assert.Null(await Api.DatasheetAsync(productId));
        Assert.Equal(1, Pages(await Api.QuotationPdfAsync(q.Id)));

        // Users cannot add one
        var ravi = await Server.LoginAsStandardAsync();
        await Assert.ThrowsAsync<ApiException>(() => ravi.UploadDatasheetAsync(productId, "a.pdf", onePage));
    }

    [Fact]
    public async Task The_evening_summary_counts_the_day_and_is_emailed_once_when_due()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);
        await Api.CreateQuotationAsync(await BevelRequest());           // a draft

        var summary = await Api.EveningSummaryAsync(Today);
        Assert.StartsWith("Quotations · ", summary.Text);
        Assert.Contains($"📄 1 quotation · ₹ {Quotation.Core.Calculation.IndianFormat.Amount(approved.GrandTotal, 0)}", summary.Text);
        Assert.Contains("📝 1 draft still open", summary.Text);

        using var scope = Server.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Services.EveningSummaryService>();
        Assert.False(await service.SendIfDueAsync(default));               // switched off
        var all = await Api.SettingsAsync();
        all.Quotation.EveningSummary = true;
        all.Quotation.EveningSummaryTime = "00:00";
        all.Quotation.EveningSummaryEmail = "owner@example.com";
        await Api.SaveSettingsAsync(all);
        Assert.False(await service.SendIfDueAsync(default));               // no Gmail connection
        Assert.Contains("Connect Gmail", (await Api.EveningSummaryAsync(Today)).Error);

        Server.Gmail = new TestSupport.FakeGmail();
        var settingsService = scope.ServiceProvider.GetRequiredService<Services.SettingsService>();
        await settingsService.RemoveAsync(Services.EveningSummaryService.StateKey);
        Assert.True(await service.SendIfDueAsync(default));
        var sent = Assert.Single(Server.Gmail.Sent);
        Assert.Equal("owner@example.com", sent.To);
        Assert.StartsWith("Quotations · ", sent.Subject);
        Assert.Contains("📄 1 quotation", sent.Body);
        Assert.False(await service.SendIfDueAsync(default));               // once a day
        Assert.Single(Server.Gmail.Sent);
        Assert.Equal(new TimeOnly(19, 0), Services.EveningSummaryService.DueTime("7 pm"));
    }
}
