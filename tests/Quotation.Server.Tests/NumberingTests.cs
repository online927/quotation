using System.Net;
using Microsoft.EntityFrameworkCore;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;

namespace Quotation.Server.Tests;

public class NumberingTests : QuotationTestBase
{
    private FinancialYear ActiveFy => FinancialYear.For(Today);

    [Fact]
    public async Task First_quotation_of_the_year_gets_sequence_one()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(QuotationNumberFormatter.Format("TSQ{FY}-{SEQ}", ActiveFy, 1), q.Number);
        Assert.Equal(ActiveFy.Label, q.FinancialYear);
        Assert.Equal(QuotationStatus.Draft, q.Status);
    }

    [Fact]
    public async Task Preview_does_not_consume_numbers()
    {
        var preview1 = await Api.NextNumberAsync();
        var preview2 = await Api.NextNumberAsync();
        Assert.Equal(preview1.Number, preview2.Number);
        Assert.True(preview1.Provisional);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(preview1.Number, q.Number);
    }

    [Fact]
    public async Task Series_can_continue_from_existing_tally_numbers()
    {
        await Api.SetNextSequenceAsync(ActiveFy.StartYear, 3248);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.EndsWith("-3248", q.Number);

        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.SetNextSequenceAsync(ActiveFy.StartYear, 3248));
        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        var series = await Api.NumberSeriesAsync();
        Assert.Equal(3249, series.NextSequence);
        Assert.Equal(3249, series.MinimumAllowed);
    }

    [Fact]
    public async Task Concurrent_creation_from_many_pcs_never_duplicates()
    {
        var request = await BevelRequest();
        var clients = new List<QuotationApiClient>();
        for (var pc = 1; pc <= 5; pc++) clients.Add(await Server.LoginAsAdminAsync($"CLIENT-PC-{pc}"));

        var tasks = Enumerable.Range(0, 60).Select(i => clients[i % clients.Count].CreateQuotationAsync(request)).ToList();
        var results = await Task.WhenAll(tasks);

        var numbers = results.Select(r => r.Number!).ToList();
        Assert.Equal(60, numbers.Distinct().Count());
        var sequences = numbers.Select(n => QuotationNumberFormatter.TryParseSequence("TSQ{FY}-{SEQ}", ActiveFy, n)!.Value).OrderBy(s => s).ToList();
        Assert.Equal(Enumerable.Range(1, 60), sequences); // no gaps, no duplicates
    }

    [Fact]
    public async Task Database_rejects_duplicate_numbers_as_last_line_of_defence()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var ex = Record.Exception(() => Server.Db(db =>
        {
            db.Quotations.Add(new Quotation.Data.Entities.QuotationHeader
            {
                Id = Guid.NewGuid(), Number = q.Number, Date = Today, CreatedBy = "x", CreatedUtc = DateTime.UtcNow,
            });
            return db.SaveChanges();
        }));
        Assert.IsType<DbUpdateException>(ex);
    }

    [Fact]
    public async Task Cancelled_numbers_are_never_reused()
    {
        var q1 = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.CancelQuotationAsync(q1.Id);
        var q2 = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.NotEqual(q1.Number, q2.Number);
        Assert.Equal(QuotationStatus.Cancelled, (await Api.QuotationAsync(q1.Id)).Status);
    }

    [Fact]
    public async Task Financial_year_transition_starts_a_new_series()
    {
        await Api.CreateQuotationAsync(await BevelRequest());
        await Api.CreateQuotationAsync(await BevelRequest());

        // Tally's active period moves to the next financial year.
        var settings = await Api.SettingsAsync();
        settings.Tally.ActiveFinancialYearOverride = ActiveFy.StartYear + 1;
        await Api.SaveSettingsAsync(settings);

        var request = await BevelRequest();
        request.Date = ActiveFy.Next().StartDate;
        var q = await Api.CreateQuotationAsync(request);
        Assert.Equal(QuotationNumberFormatter.Format("TSQ{FY}-{SEQ}", ActiveFy.Next(), 1), q.Number);
        Assert.Equal(ActiveFy.Next().Label, q.FinancialYear);
    }

    [Fact]
    public async Task Date_outside_active_financial_year_is_rejected()
    {
        var request = await BevelRequest();
        request.Date = ActiveFy.Previous().EndDate;
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.CreateQuotationAsync(request));
        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        Assert.Contains("outside the active financial year", ex.Message);
    }

    [Fact]
    public async Task Pattern_change_applies_to_new_numbers()
    {
        var settings = await Api.SettingsAsync();
        settings.Quotation.NumberPattern = "QT/{FY_LABEL}/{SEQ:4}";
        await Api.SaveSettingsAsync(settings);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.Equal($"QT/{ActiveFy.Label}/0001", q.Number);
    }
}
