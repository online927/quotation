using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>
/// Round 21: only the administrator cancels a quotation that has been approved; "users change only their own";
/// descriptions remembered only for items with a part number; "open the PDF after Save as PDF".
/// </summary>
public class Round21Tests : QuotationTestBase
{
    public Round21Tests() => Server.AdministratorsMakeDocuments = false;

    private async Task<SaveQuotationRequest> RequestAsync(string product = "187-901-10-UNIVERSAL BEVEL PROTRACTOR", string description = "")
    {
        var request = await BevelRequest();
        var p = await Product(product);
        request.Lines = [new QuotationLineDto { ProductId = p.Id, Quantity = 1, Rate = p.Rate ?? 100, TallyRate = p.Rate, Description = description }];
        return request;
    }

    [Fact]
    public async Task Approved_quotations_are_cancelled_only_by_the_administrator()
    {
        var ravi = await Server.LoginAsStandardAsync();

        // Never approved: the user cancels it as before.
        var draft = await ravi.CreateQuotationAsync(await RequestAsync());
        Assert.Equal(QuotationStatus.Cancelled, (await ravi.CancelQuotationAsync(draft.Id)).Status);

        var q = await ravi.CreateQuotationAsync(await RequestAsync());
        var approved = await ravi.ApproveQuotationAsync(q.Id);
        Assert.True(approved.EverApproved);
        var refused = await Assert.ThrowsAsync<ApiException>(() => ravi.CancelQuotationAsync(q.Id));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains(QuotationService.OnlyAdminCancelsApproved, refused.Message);

        // Edited back to draft: the user still edits it but cannot cancel it.
        var change = await RequestAsync();
        change.Lines[0].Quantity = 3;
        change.Revision = approved.Revision;
        var edited = await ravi.UpdateQuotationAsync(q.Id, change);
        Assert.Equal(QuotationStatus.Draft, edited.Status);
        Assert.True(edited.EverApproved);
        await Assert.ThrowsAsync<ApiException>(() => ravi.CancelQuotationAsync(q.Id));

        // The administrator can.
        Assert.Equal(QuotationStatus.Cancelled, (await Api.CancelQuotationAsync(q.Id)).Status);

        // The same for proforma invoices.
        await TestServer.EnableProformasAsync(Api);
        var other = await ravi.CreateQuotationAsync(await RequestAsync());
        var proforma = await ravi.ConvertToProformaAsync(other.Id);
        await ravi.ApproveQuotationAsync(proforma.Id);
        await Assert.ThrowsAsync<ApiException>(() => ravi.CancelQuotationAsync(proforma.Id));
    }

    [Fact]
    public async Task With_the_switch_on_users_look_at_but_do_not_change_each_others_quotations()
    {
        var ravi = await Server.LoginAsStandardAsync("ravi");
        var asha = await Server.LoginAsStandardAsync("asha");
        var raviDraft = await ravi.CreateQuotationAsync(await RequestAsync());
        Assert.Equal("ravi", raviDraft.Owner);

        // Off (the default): Asha may work on Ravi's quotation.
        var change = await RequestAsync();
        change.Revision = raviDraft.Revision;
        raviDraft = await asha.UpdateQuotationAsync(raviDraft.Id, change);

        var all = await Api.SettingsAsync();
        all.Quotation.UsersChangeOnlyTheirOwn = true;
        await Api.SaveSettingsAsync(all);

        change.Revision = raviDraft.Revision;
        var refused = await Assert.ThrowsAsync<ApiException>(() => asha.UpdateQuotationAsync(raviDraft.Id, change));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains("belongs to ravi", refused.Message);
        await Assert.ThrowsAsync<ApiException>(() => asha.ApproveQuotationAsync(raviDraft.Id));
        await Assert.ThrowsAsync<ApiException>(() => asha.DuplicateQuotationAsync(raviDraft.Id));
        await Assert.ThrowsAsync<ApiException>(() => asha.CancelQuotationAsync(raviDraft.Id));
        // Looking is fine: open it, preview it, see it in the list.
        Assert.Equal(raviDraft.Id, (await asha.QuotationAsync(raviDraft.Id)).Id);
        Assert.NotEmpty(await asha.PreviewPdfAsync(raviDraft.Id));
        Assert.Contains(await asha.QuotationsAsync(), s => s.Id == raviDraft.Id && s.Owner == "ravi");

        // Ravi approves his own; Asha cannot print it (Save as PDF / WhatsApp).
        await ravi.ApproveQuotationAsync(raviDraft.Id);
        await Assert.ThrowsAsync<ApiException>(() => asha.QuotationPdfAsync(raviDraft.Id));
        Assert.NotEmpty(await ravi.QuotationPdfAsync(raviDraft.Id));

        // A draft Gmail made belongs to the first user who saves it.
        var gmailDraft = await ravi.CreateQuotationAsync(await RequestAsync());
        Server.Db(db =>
        {
            var row = db.Quotations.Single(x => x.Id == gmailDraft.Id);
            row.CreatedBy = QuotationService.GmailUserName;
            row.Owner = null;
            return db.SaveChanges();
        });
        var claim = await RequestAsync();
        claim.Revision = gmailDraft.Revision;
        Assert.Equal("asha", (await asha.UpdateQuotationAsync(gmailDraft.Id, claim)).Owner);
        claim.Revision++;
        await Assert.ThrowsAsync<ApiException>(() => ravi.UpdateQuotationAsync(gmailDraft.Id, claim));
    }

    [Fact]
    public async Task Descriptions_are_remembered_only_for_items_with_a_part_number()
    {
        var ravi = await Server.LoginAsStandardAsync();
        var withPart = await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        var noPart = (await Api.SearchProductsAsync("vernier", 10)).First(h => string.IsNullOrWhiteSpace(h.Product.PartNumber)).Product;
        Assert.False(string.IsNullOrWhiteSpace(withPart.PartNumber));

        await ravi.CreateQuotationAsync(await RequestAsync(withPart.Name, "Calibrated, with certificate"));
        await ravi.CreateQuotationAsync(await RequestAsync(noPart.Name, "Special size for this customer"));

        Assert.Equal("Calibrated, with certificate", (await Product(withPart.Name)).Description);
        Assert.Equal(noPart.Description, (await Api.SearchProductsAsync(noPart.Name, 10)).First(h => h.Product.Id == noPart.Id).Product.Description);
    }

    [Fact]
    public async Task Open_pdf_after_save_is_on_until_the_user_switches_it_off()
    {
        var ravi = await Server.LoginAsStandardAsync();
        Assert.True((await ravi.MeAsync()).OpenPdfAfterSave);
        var me = await ravi.SetMyPreferencesAsync(new MyPreferencesDto(OpenPdfAfterSave: false));
        Assert.False(me.OpenPdfAfterSave);
        Assert.False((await ravi.MeAsync()).OpenPdfAfterSave);
    }
}
