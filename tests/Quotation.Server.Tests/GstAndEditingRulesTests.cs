using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>GST lines by default, "Without GST", the users' editing period, and deleting with the Deleted list.</summary>
public class GstAndEditingRulesTests : QuotationTestBase
{
    private FinancialYear ActiveFy => FinancialYear.For(Today);

    private static string PdfText(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    /// <summary>The editable content of a quotation as the desktop app sends it back.</summary>
    internal static SaveQuotationRequest Req(QuotationDto q) => new()
    {
        Revision = q.Revision, DocType = q.DocType, Date = q.Date, CustomerId = q.CustomerId, Buyer = q.Buyer, Consignee = q.Consignee,
        ConsigneeSameAsBuyer = q.ConsigneeSameAsBuyer, BuyersReference = q.BuyersReference, BuyersReferenceDate = q.BuyersReferenceDate,
        DispatchedThrough = q.DispatchedThrough, PaymentTerms = q.PaymentTerms, OtherReferences = q.OtherReferences, Destination = q.Destination,
        TermsOfDelivery = q.TermsOfDelivery, Remarks = q.Remarks, TermsAndConditions = q.TermsAndConditions, PackingForwarding = q.PackingForwarding,
        PackingForwardingPercent = q.PackingForwardingPercent, PreparedBy = q.PreparedBy, VerifiedBy = q.VerifiedBy, WithoutGst = q.WithoutGst,
        Lines = q.Lines,
    };

    private async Task<QuotationApiClient> UserAsync(string name = "keerthana")
    {
        await Api.CreateUserAsync(new CreateUserRequest(name, "Keerthana", "first-pass", UserRole.User));
        var api = Server.CreateApi("USER-PC");
        await api.LoginAsync(name, "first-pass");
        await api.ChangePasswordAsync("first-pass", "user-pass");
        return api;
    }

    [Fact]
    public async Task Gst_lines_and_round_off_are_the_default_and_a_saved_quotation_keeps_its_own()
    {
        var settings = await Api.SettingsAsync();
        Assert.Equal(TaxPresentation.ComputeTax, settings.Quotation.TaxPresentation);
        Assert.True(settings.Quotation.RoundOffTotal);

        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(36816m, q.GrandTotal);

        // Changing the setting later does not change quotations already saved, nor their print.
        settings.Quotation.TaxPresentation = TaxPresentation.RateOnly;
        await Api.SaveSettingsAsync(settings);
        var reloaded = await Api.QuotationAsync(q.Id);
        Assert.Equal(36816m, reloaded.GrandTotal);
        Assert.Equal(["CGST", "SGST"], reloaded.Taxes.Select(t => t.Name).Order());
        await Api.ApproveQuotationAsync(q.Id);
        var text = PdfText(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("SGST", text);
        Assert.Contains("36,816.00", text);
    }

    [Fact]
    public async Task Servers_installed_earlier_switch_to_gst_lines_once()
    {
        var settingsService = Server.Services.GetRequiredService<SettingsService>();
        var old = settingsService.Quotation;
        old.TaxPresentation = TaxPresentation.RateOnly;
        old.RoundOffTotal = false;
        await settingsService.SaveAsync(SettingsService.QuotationKey, old, "test");
        Server.Db(db => db.Settings.Where(s => s.Key == SettingsService.UpgradeGstDefaultKey).ExecuteDelete());
        // (the cache still says "done": a restart reads the database again)
        var fresh = new SettingsService(Server.Services.GetRequiredService<IServiceScopeFactory>(),
            Server.Services.GetRequiredService<Quotation.Server.Infrastructure.ISecretProtector>(), TimeProvider.System);

        await fresh.ApplyUpgradesAsync(CancellationToken.None);
        Assert.Equal(TaxPresentation.ComputeTax, fresh.Quotation.TaxPresentation);
        Assert.True(fresh.Quotation.RoundOffTotal);

        // Once only: an administrator who chooses RateOnly afterwards keeps it.
        var chosen = fresh.Quotation;
        chosen.TaxPresentation = TaxPresentation.RateOnly;
        await fresh.SaveAsync(SettingsService.QuotationKey, chosen, "admin");
        await fresh.ApplyUpgradesAsync(CancellationToken.None);
        Assert.Equal(TaxPresentation.RateOnly, fresh.Quotation.TaxPresentation);
    }

    [Fact]
    public async Task Without_gst_is_for_one_quotation_and_prints_like_tally()
    {
        var me = await Api.SetMyPreferencesAsync(new MyPreferencesDto(true));
        Assert.True(me.WithoutGstNext);

        var request = await BevelRequest();
        request.WithoutGst = true;
        request.PackingForwarding = 180;
        var q = await Api.CreateQuotationAsync(request);
        Assert.True(q.WithoutGst);
        Assert.Empty(q.Taxes);
        Assert.Equal(0m, q.TaxTotal);
        Assert.Equal(31380m, q.GrandTotal); // items + P&F, no GST
        Assert.False((await Api.MeAsync()).WithoutGstNext); // switched itself off

        await Api.ApproveQuotationAsync(q.Id);
        var text = PdfText(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("Packing & Forwarding - Without GST", text);
        Assert.Contains("0 %", text);
        Assert.DoesNotContain("SGST", text);
        Assert.DoesNotContain("CGST", text);

        // Editing keeps it; a duplicate is a normal quotation.
        var edited = await Api.UpdateQuotationAsync(q.Id, Req(await Api.QuotationAsync(q.Id)));
        Assert.True(edited.WithoutGst);
        var copy = await Api.DuplicateQuotationAsync(q.Id);
        Assert.False(copy.WithoutGst);
        Assert.NotEmpty(copy.Taxes);
    }

    [Fact]
    public async Task Users_can_edit_for_seven_days_administrators_always()
    {
        var user = await UserAsync();
        var q = await user.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(Today.AddDays(7), q.EditableUntil);
        var request = Req(q);
        request.Remarks = "within 7 days";
        q = await user.UpdateQuotationAsync(q.Id, request);

        // Made 8 days ago: locked for the user, also on the server.
        Server.Db(db => db.Quotations.Where(x => x.Id == q.Id).ExecuteUpdate(s => s.SetProperty(x => x.CreatedUtc, DateTime.UtcNow.AddDays(-8))));
        request = Req(await user.QuotationAsync(q.Id));
        request.Remarks = "too late";
        var ex = await Assert.ThrowsAsync<ApiException>(() => user.UpdateQuotationAsync(q.Id, request));
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
        Assert.Equal(ApiErrorCodes.Locked, ex.Code);
        Assert.Equal(ApiErrorCodes.Locked, (await Assert.ThrowsAsync<ApiException>(() => user.CancelQuotationAsync(q.Id))).Code);
        Assert.Equal(ApiErrorCodes.Locked, (await Assert.ThrowsAsync<ApiException>(() => user.ApproveQuotationAsync(q.Id))).Code);

        // The user can still duplicate it; an administrator can still change it.
        var copy = await user.DuplicateQuotationAsync(q.Id);
        Assert.NotEqual(q.Id, copy.Id);
        var byAdmin = await Api.UpdateQuotationAsync(q.Id, request);
        Assert.Equal("too late", byAdmin.Remarks);
    }

    [Fact]
    public async Task Deleting_the_latest_number_reuses_it_and_restore_brings_it_back_when_free()
    {
        var q1 = await Api.CreateQuotationAsync(await BevelRequest());
        var q2 = await Api.CreateQuotationAsync(await BevelRequest());
        string N(int seq) => QuotationNumberFormatter.Format("TSQ{FY}-{SEQ}", ActiveFy, seq);
        Assert.Equal(N(2), q2.Number);

        // Latest number deleted: the next quotation gets it again.
        await Api.DeleteQuotationAsync(q2.Id);
        Assert.DoesNotContain(await Api.QuotationsAsync(), s => s.Id == q2.Id);
        await Assert.ThrowsAsync<ApiException>(() => Api.QuotationAsync(q2.Id));
        var deleted = Assert.Single(await Api.DeletedQuotationsAsync());
        Assert.Equal(N(2), deleted.Number);
        Assert.True(deleted.NumberStillFree);
        var q3 = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(N(2), q3.Number);

        // Its number is taken now: restored as a draft without a number, which gets the next number when saved.
        Assert.False((await Api.DeletedQuotationsAsync()).Single().NumberStillFree);
        var restored = await Api.RestoreQuotationAsync(q2.Id);
        Assert.False(restored.NumberKept);
        Assert.Null(restored.Quotation.Number);
        var saved = await Api.UpdateQuotationAsync(q2.Id, Req(restored.Quotation));
        Assert.Equal(N(3), saved.Number);

        // An older number leaves a gap and comes back with a restore.
        await Api.DeleteQuotationAsync(q1.Id);
        Assert.Equal(N(4), (await Api.NextNumberAsync()).Number);
        var back = await Api.RestoreQuotationAsync(q1.Id);
        Assert.True(back.NumberKept);
        Assert.Equal(N(1), back.Quotation.Number);
    }

    [Fact]
    public async Task Only_administrators_delete_and_deleted_documents_go_after_30_days()
    {
        var user = await UserAsync();
        var q = await user.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => user.DeleteQuotationAsync(q.Id))).Status);

        await Api.DeleteQuotationAsync(q.Id);
        Server.Db(db => db.Quotations.IgnoreQueryFilters().Where(x => x.Id == q.Id)
            .ExecuteUpdate(s => s.SetProperty(x => x.DeletedUtc, DateTime.UtcNow.AddDays(-31))));
        using (var scope = Server.Services.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<QuotationService>().PurgeExpiredAsync(CancellationToken.None));
        }
        Assert.Empty(await Api.DeletedQuotationsAsync());
        Assert.Equal(0, Server.Db(db => db.Quotations.IgnoreQueryFilters().Count(x => x.Id == q.Id)));
        var audit = await Api.AuditAsync("Quotation", q.Id.ToString());
        Assert.Contains(audit, a => a.Action == "QuotationDeleted");
        Assert.Contains(audit, a => a.Action == "QuotationDeletedPermanently");
    }
}
