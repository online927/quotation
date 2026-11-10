using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data.Entities;
using Quotation.Server.Auth;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>
/// Round 18: the administrator makes no quotations, one administrator and Standard users, separate Contact and E-mail
/// lines, and proforma invoices switched off at first.
/// </summary>
public class Round18Tests : QuotationTestBase
{
    // The real office rule (the other tests let "admin" make quotations).
    public Round18Tests() => Server.AdministratorsMakeDocuments = false;

    [Fact]
    public async Task Administrator_only_opens_cancels_and_deletes_documents()
    {
        var ravi = await Server.LoginAsStandardAsync();
        Assert.True(ravi.CurrentUser!.MakesDocuments);
        Assert.False(Api.CurrentUser!.MakesDocuments);
        Assert.False((await Api.MeAsync()).MakesDocuments);

        var refused = await Assert.ThrowsAsync<ApiException>(async () => await Api.CreateQuotationAsync(await BevelRequest()));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains(QuotationService.AdminsDoNotMakeDocuments, refused.Message);

        var draft = await ravi.CreateQuotationAsync(await BevelRequest());
        var change = await BevelRequest(qty: 5);
        change.Revision = draft.Revision;
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => Api.UpdateQuotationAsync(draft.Id, change))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(draft.Id))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => Api.DuplicateQuotationAsync(draft.Id))).Status);

        // Opening, the PDF preview, cancelling and deleting stay with the administrator.
        Assert.Equal(draft.Id, (await Api.QuotationAsync(draft.Id)).Id);
        Assert.NotEmpty(await Api.PreviewPdfAsync(draft.Id));
        Assert.Equal(QuotationStatus.Cancelled, (await Api.CancelQuotationAsync(draft.Id)).Status);
        await Api.DeleteQuotationAsync(draft.Id);

        // Ravi makes and approves quotations as before.
        var second = await ravi.CreateQuotationAsync(await BevelRequest());
        Assert.Equal(QuotationStatus.Generated, (await ravi.ApproveQuotationAsync(second.Id)).Status);
    }

    [Fact]
    public async Task There_is_one_administrator_and_new_users_are_standard()
    {
        var create = await Assert.ThrowsAsync<ApiException>(() => Api.CreateUserAsync(new CreateUserRequest("boss", "Boss", "boss-pass", UserRole.Admin)));
        Assert.Contains(AuthService.OnlyOneAdmin, create.Message);

        var ravi = await Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        Assert.Equal("Standard", ravi.RoleName);
        var promote = await Assert.ThrowsAsync<ApiException>(() => Api.UpdateUserAsync(ravi.Id, new UpdateUserRequest("ravi", "Ravi", UserRole.Admin, true)));
        Assert.Contains(AuthService.OnlyOneAdmin, promote.Message);
        Assert.Equal("Administrator", (await Api.UsersAsync()).Single(u => u.Username == "admin").RoleName);
    }

    [Fact]
    public async Task Upgrade_keeps_admin_as_the_only_administrator()
    {
        // Before Round 18 a second administrator could be made.
        var ravi = await Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        Server.Db(db =>
        {
            db.Users.Single(u => u.Id == ravi.Id).Role = UserRole.Admin;
            return db.SaveChanges();
        });

        using (var scope = Server.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureBootstrapAdminAsync(CancellationToken.None);

        var users = await Api.UsersAsync();
        Assert.Equal(UserRole.Admin, users.Single(u => u.Username == "admin").Role);
        Assert.Equal(UserRole.User, users.Single(u => u.Username == "ravi").Role);
    }

    [Fact]
    public async Task Proforma_invoices_are_off_until_switched_on_and_old_ones_stay_in_history()
    {
        Assert.False((await Api.SettingsAsync()).Quotation.ProformaEnabled);
        var ravi = await Server.LoginAsStandardAsync();
        var q = await ravi.CreateQuotationAsync(await BevelRequest());
        await ravi.ApproveQuotationAsync(q.Id);

        var convert = await Assert.ThrowsAsync<ApiException>(() => ravi.ConvertToProformaAsync(q.Id));
        Assert.Equal(HttpStatusCode.Conflict, convert.Status);
        Assert.Contains("switched off", convert.Message);
        var direct = await BevelRequest();
        direct.DocType = DocumentType.ProformaInvoice;
        await Assert.ThrowsAsync<ApiException>(() => ravi.CreateQuotationAsync(direct));

        await TestServer.EnableProformasAsync(Api);
        var proforma = await ravi.ConvertToProformaAsync(q.Id);

        var all = await Api.SettingsAsync();
        all.Quotation.ProformaEnabled = false;
        await Api.SaveSettingsAsync(all);
        Assert.Contains(await ravi.QuotationsAsync(), s => s.Id == proforma.Id && s.DocType == DocumentType.ProformaInvoice);
    }

    private static readonly PartySnapshot Party = new()
    {
        Name = "Sonepar India Private Limited", Address = "Chakan, Pune", ContactPerson = "Mr. Rahul",
        Phone = "9822012345", Email = "purchase.pune@sonepar.example",
    };

    [Fact]
    public void Contact_and_email_lines_have_their_own_tick_boxes()
    {
        // Settings saved before Round 18: the e-mail follows the old "Contact details" tick.
        var old = JsonSerializer.Deserialize<PartyPrintSettings>("""{"ShowContactDetails":false}""")!;
        Assert.False(old.ShowEmail);
        Assert.True(JsonSerializer.Deserialize<PartyPrintSettings>("""{"ShowContactDetails":true}""")!.ShowEmail);

        var phoneOnly = PdfQuotationRenderer.ToParty(Party, new PartyPrintSettings { ShowEmail = false, ShowContactPerson = true }, null);
        Assert.Equal(("Mr. Rahul", "9822012345", ""), (phoneOnly.ContactPerson, phoneOnly.Phone, phoneOnly.Email));

        var emailOnly = PdfQuotationRenderer.ToParty(Party, new PartyPrintSettings { ShowContactDetails = false, ShowContactPerson = true, ShowEmail = true }, null);
        Assert.Equal(("", "", "purchase.pune@sonepar.example"), (emailOnly.ContactPerson, emailOnly.Phone, emailOnly.Email));

        // Saved and read back: the e-mail choice is kept apart from the contact choice.
        var json = JsonSerializer.Serialize(new PartyPrintSettings { ShowContactDetails = true, ShowEmail = false });
        Assert.False(JsonSerializer.Deserialize<PartyPrintSettings>(json)!.ShowEmail);
    }

    [Fact]
    public async Task Passwords_of_four_characters_are_allowed()
    {
        var tooShort = await Assert.ThrowsAsync<ApiException>(() => Api.CreateUserAsync(new CreateUserRequest("asha", "Asha", "123", UserRole.User)));
        Assert.Contains(tooShort.Details, d => d.Contains("at least 4"));

        await Api.CreateUserAsync(new CreateUserRequest("asha", "Asha", "1234", UserRole.User));
        var asha = Server.CreateApi();
        await asha.LoginAsync("asha", "1234");
        await asha.ChangePasswordAsync("1234", "4321");
        await Server.CreateApi().LoginAsync("asha", "4321");
    }
}
