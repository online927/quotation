using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 35: GST by the buyer's state or the ship-to state ("bill to – ship to").</summary>
public class Round35Tests : QuotationTestBase
{
    /// <summary>Our company in Karnataka; the buyer Sonepar in Maharashtra; the goods to their Bangalore warehouse.</summary>
    private async Task<SaveQuotationRequest> BillToPuneShipToBangaloreAsync()
    {
        var settings = await Api.SettingsAsync();
        settings.Company.StateCode = "29";
        settings.Quotation.TaxPresentation = TaxPresentation.ComputeTax;
        await Api.SaveSettingsAsync(settings);
        var request = await BevelRequest();
        request.ConsigneeSameAsBuyer = false;
        request.Consignee = new PartyDto { Name = "Sonepar Bangalore Warehouse", Address = "Peenya", StateName = "Karnataka", StateCode = "29" };
        return request;
    }

    [Fact]
    public async Task Gst_follows_the_buyers_state_when_chosen_and_the_choice_is_kept()
    {
        var request = await BillToPuneShipToBangaloreAsync();
        request.GstByBuyer = true;
        var q = await Api.CreateQuotationAsync(request);
        Assert.True(q.GstByBuyer);
        Assert.Equal(["IGST"], q.Taxes.Select(t => t.Name));                 // the buyer is in Maharashtra
        Assert.Equal(31200m + 5616m, q.GrandTotal);

        // Saved again without saying (an older app): the quotation keeps its own choice
        request.Revision = q.Revision;
        request.GstByBuyer = null;
        var again = await Api.UpdateQuotationAsync(q.Id, request);
        Assert.True(again.GstByBuyer);
        Assert.Equal(["IGST"], again.Taxes.Select(t => t.Name));

        // The ship-to state instead
        request.Revision = again.Revision;
        request.GstByBuyer = false;
        var shipTo = await Api.UpdateQuotationAsync(q.Id, request);
        Assert.False(shipTo.GstByBuyer);
        Assert.Equal(["CGST", "SGST"], shipTo.Taxes.Select(t => t.Name).Order());

        // Read back and duplicated: the same choice
        Assert.False((await Api.QuotationAsync(q.Id)).GstByBuyer);
        Assert.False((await Api.DuplicateQuotationAsync(q.Id)).GstByBuyer);
    }

    [Fact]
    public async Task Without_a_choice_the_ship_to_state_is_followed_as_before()
    {
        // An app that does not know the choice yet (and every quotation made before it): as before
        var request = await BillToPuneShipToBangaloreAsync();
        var q = await Api.CreateQuotationAsync(request);
        Assert.False(q.GstByBuyer);
        Assert.Equal(["CGST", "SGST"], q.Taxes.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task An_administrator_deletes_a_user_and_their_quotations_keep_their_name()
    {
        var ravi = await Server.LoginAsStandardAsync("ravi");
        var q = await ravi.CreateQuotationAsync(await BevelRequest());
        var users = await Api.UsersAsync();
        var raviId = users.Single(u => u.Username == "ravi").Id;
        var adminId = users.Single(u => u.Role == UserRole.Admin).Id;

        // Never the administrator (nor yourself), and only an administrator deletes
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() => Api.DeleteUserAsync(adminId))).Status);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => ravi.DeleteUserAsync(adminId))).Status);

        await Api.DeleteUserAsync(raviId);
        Assert.DoesNotContain(await Api.UsersAsync(), u => u.Username == "ravi");
        // Signed out on every PC, and cannot sign in again
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => ravi.StatusAsync())).Status);
        await Assert.ThrowsAsync<ApiException>(() => Server.CreateApi("OTHER-PC").LoginAsync("ravi", "ravi-pass"));
        // Their quotation stays, with their name
        Assert.Equal("ravi", (await Api.QuotationAsync(q.Id)).CreatedBy);
        // Already gone
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await Assert.ThrowsAsync<ApiException>(() => Api.DeleteUserAsync(raviId))).Status);
    }

    [Fact]
    public async Task With_the_seal_pin_switch_off_a_user_seals_with_only_a_picture()
    {
        Assert.False((await Api.SettingsAsync()).Quotation.SealPin); // off by default
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        var ravi = await Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        var user = Server.CreateApi("USER-PC");
        await user.LoginAsync("ravi", "ravi-pass");
        Assert.Equal(ApiErrorCodes.InvalidState, (await Assert.ThrowsAsync<ApiException>(() => user.SealedPdfAsync(q.Id, ""))).Code); // no picture

        await Api.UploadUserSealAsync(ravi.Id, Convert.FromBase64String(UsersDnuSealTests.Png)); // a picture, no PIN
        Assert.NotEmpty(await user.SealedPdfAsync(q.Id, ""));
        Assert.Equal(ApiErrorCodes.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => Api.SealedPdfAsync(q.Id, ""))).Code); // administrators never

        var on = await Api.SettingsAsync();
        on.Quotation.SealPin = true;
        await Api.SaveSettingsAsync(on);
        Assert.Equal(ApiErrorCodes.InvalidState, (await Assert.ThrowsAsync<ApiException>(() => user.SealedPdfAsync(q.Id, ""))).Code); // now a PIN is needed
    }

    private static string PdfWords(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public async Task The_minimal_layout_and_a_delivery_time_per_line_are_printed()
    {
        var request = await BevelRequest();
        request.Lines[0].DueOn = "2–3 weeks";
        var q = await Api.CreateQuotationAsync(request);
        Assert.Equal("2–3 weeks", q.Lines[0].DueOn);
        await Api.ApproveQuotationAsync(q.Id);
        Assert.Contains("Delivery: 2–3 weeks", PdfWords(await Api.QuotationPdfAsync(q.Id))); // the user's layout (Tally)

        await Api.SetMyPreferencesAsync(new MyPreferencesDto(PdfLayout: PdfLayouts.Minimal));
        var minimal = PdfWords(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("Q U O T A T I O N", minimal);
        Assert.Contains("BUYER", minimal);
        Assert.Contains("REFERENCE", minimal);
        Assert.Contains("Delivery: 2–3 weeks", minimal);
        Assert.Contains("Authorised Signatory", minimal);
        Assert.Contains("Q U O T A T I O N", PdfWords(await Api.PreviewPdfAsync(q.Id)));
    }
}
