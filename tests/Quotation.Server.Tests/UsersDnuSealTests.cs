using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Auth;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>User management, "Do Not Use" items and the company seal.</summary>
public class UsersDnuSealTests : QuotationTestBase
{
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAJUlEQVR4nGM4ICDwn5aYYdSCUQtGLRi1YNSCUQtGLRi1YGhYAAC4lDX9H0TkSwAAAABJRU5ErkJggg==";

    private async Task<(QuotationApiClient Api, UserDto User)> UserAsync(string name = "keerthana")
    {
        var created = await Api.CreateUserAsync(new CreateUserRequest(name, "Keerthana", "first-pass", UserRole.User));
        var api = Server.CreateApi("USER-PC");
        await api.LoginAsync(name, "first-pass");
        await api.ChangePasswordAsync("first-pass", "user-pass");
        return (api, created);
    }

    [Fact]
    public async Task Administrator_renames_changes_role_and_resets_password()
    {
        var (user, dto) = await UserAsync();
        var renamed = await Api.UpdateUserAsync(dto.Id, new UpdateUserRequest("keerthana.s", "Keerthana S", UserRole.Admin, true));
        Assert.Equal("keerthana.s", renamed.Username);
        Assert.Equal(UserRole.Admin, renamed.Role);
        Assert.Equal("Keerthana S", (await user.MeAsync()).DisplayName); // still logged in after a rename

        // Reset: logged out everywhere, a temporary password that must be changed.
        await Api.ResetPasswordAsync(dto.Id, "temp-123");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => user.MeAsync())).Status);
        var again = Server.CreateApi("USER-PC");
        var login = await again.LoginAsync("keerthana.s", "temp-123");
        Assert.True(login.User.MustChangePassword);

        // A login name can only be used once.
        var other = await Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        var dup = await Assert.ThrowsAsync<ApiException>(() => Api.UpdateUserAsync(other.Id, new UpdateUserRequest("KEERTHANA.S", "Ravi", UserRole.User, true)));
        Assert.Contains("already used", dup.Message);
    }

    [Fact]
    public async Task An_administrator_cannot_lock_out_themselves_or_the_last_administrator()
    {
        var me = await Api.MeAsync();
        Assert.Contains("own administrator role", (await Assert.ThrowsAsync<ApiException>(() =>
            Api.UpdateUserAsync(me.Id, new UpdateUserRequest(me.Username, me.DisplayName, UserRole.User, true)))).Message);
        Assert.Contains("own account", (await Assert.ThrowsAsync<ApiException>(() =>
            Api.UpdateUserAsync(me.Id, new UpdateUserRequest(me.Username, me.DisplayName, UserRole.Admin, false)))).Message);

        // A second administrator can change the first one (so a forgotten admin is never stuck), never themselves.
        var (_, second) = await UserAsync("second");
        await Api.UpdateUserAsync(second.Id, new UpdateUserRequest("second", "Second", UserRole.Admin, true));
        var secondApi = Server.CreateApi();
        await secondApi.LoginAsync("second", "user-pass");
        Assert.Equal(UserRole.User, (await secondApi.UpdateUserAsync(me.Id, new UpdateUserRequest(me.Username, me.DisplayName, UserRole.User, true))).Role);
        Assert.Contains("own administrator role", (await Assert.ThrowsAsync<ApiException>(() =>
            secondApi.UpdateUserAsync(second.Id, new UpdateUserRequest("second", "Second", UserRole.User, true)))).Message);
    }

    [Fact]
    public async Task Forgotten_admin_password_is_reset_on_the_server_pc()
    {
        using (var scope = Server.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.RecoverAdminAsync("123", null, CancellationToken.None));
            Assert.Equal("admin", await auth.RecoverAdminAsync("recovered-1", null, CancellationToken.None));
        }
        await Assert.ThrowsAsync<ApiException>(() => Api.MeAsync()); // old sessions logged out
        var api = Server.CreateApi();
        var login = await api.LoginAsync("admin", "recovered-1");
        Assert.Equal(UserRole.Admin, login.User.Role);
        Assert.False(login.User.MustChangePassword);
    }

    [Fact]
    public async Task Items_marked_dnu_in_tally_are_not_offered()
    {
        var bevel = await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        var q = await Api.CreateQuotationAsync(await BevelRequest());

        var item = Server.Tally.Data.StockItems.First(i => i.Name == bevel.Name);
        item.Description = "D.N.U. - use 187-907";
        await Api.StartSyncAsync(SyncKind.Full, wait: true);

        await WaitUntilAsync(async () => (await Api.SearchProductsAsync("universal bevel", 20)).All(h => h.Product.Id != bevel.Id));
        var withHidden = await Api.SearchProductsAsync("universal bevel", 20, includeHidden: true);
        Assert.True(withHidden.Single(h => h.Product.Id == bevel.Id).Product.Hidden);
        Assert.DoesNotContain((await Api.RecentItemsAsync()).Products, p => p.Id == bevel.Id);

        // Quotations that already have it still open, with a warning.
        var opened = await Api.QuotationAsync(q.Id);
        Assert.Single(opened.DoNotUseItems);

        // A description merely containing the letters is not a marker.
        Assert.False(Quotation.Core.Search.ProductVisibility.IsHidden("DNUT 10mm", ["DNU"]));
        Assert.True(Quotation.Core.Search.ProductVisibility.IsHidden("dnu", ["DNU"]));
        Assert.True(Quotation.Core.Search.ProductVisibility.IsHidden("DO NOT USE", ["DNU", "DO NOT USE"]));
    }

    [Fact]
    public async Task Seal_needs_the_pin_and_permission_and_blocks_after_five_wrong_tries()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        var plain = await Api.QuotationPdfAsync(q.Id);

        Assert.Equal(ApiErrorCodes.InvalidState, (await Assert.ThrowsAsync<ApiException>(() => Api.SealedPdfAsync(q.Id, "1111"))).Code);
        await Api.UploadSealAsync(Convert.FromBase64String(Png));
        var settings = await Api.SettingsAsync();
        Assert.True(settings.SealImageConfigured);
        settings.SealPin = "12a4";
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() => Api.SaveSettingsAsync(settings))).Status);
        settings.SealPin = "1111"; // repeated digits are fine
        Assert.True((await Api.SaveSettingsAsync(settings)).SealPinConfigured);

        var sealedPdf = await Api.SealedPdfAsync(q.Id, "1111");
        Assert.True(sealedPdf.Length > plain.Length);
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(sealedPdf))
            Assert.NotEmpty(doc.GetPage(doc.NumberOfPages).GetImages());
        Assert.Contains(await Api.AuditAsync("Quotation", q.Id.ToString()), a => a.Action == "PdfSealed");
        Assert.NotEmpty(await Api.SealPreviewAsync());

        // Moving the seal up (a negative downward offset) lifts it on the page, before and after saving.
        static double SealBottom(byte[] pdf)
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
            return doc.GetPage(doc.NumberOfPages).GetImages().Single().Bounds.Bottom; // PDF y grows upwards
        }
        var standard = SealBottom(await Api.SealPreviewAsync());
        var raised = SealBottom(await Api.SealPreviewAsync(offsetY: -60));
        Assert.InRange(raised - standard, 55, 65);
        settings = await Api.SettingsAsync();
        settings.Company.SealOffsetY = -60;
        await Api.SaveSettingsAsync(settings);
        Assert.InRange(SealBottom(await Api.SealedPdfAsync(q.Id, "1111")) - SealBottom(sealedPdf), 55, 65);

        // Users only when allowed, and five wrong PINs block the seal for that user.
        var user = Server.CreateApi("USER-PC");
        await Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        await user.LoginAsync("ravi", "ravi-pass");
        Assert.Equal(ApiErrorCodes.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => user.SealedPdfAsync(q.Id, "1111"))).Code);
        settings = await Api.SettingsAsync();
        settings.Quotation.AllUsersCanSeal = true;
        await Api.SaveSettingsAsync(settings);
        Assert.NotEmpty(await user.SealedPdfAsync(q.Id, "1111"));
        for (var i = 0; i < 4; i++)
            Assert.Equal(ApiErrorCodes.WrongPin, (await Assert.ThrowsAsync<ApiException>(() => user.SealedPdfAsync(q.Id, "9999"))).Code);
        Assert.Contains("blocked", (await Assert.ThrowsAsync<ApiException>(() => user.SealedPdfAsync(q.Id, "9999"))).Message);
        Assert.Equal(ApiErrorCodes.SealBlocked, (await Assert.ThrowsAsync<ApiException>(() => user.SealedPdfAsync(q.Id, "1111"))).Code);
        Assert.NotEmpty(await Api.SealedPdfAsync(q.Id, "1111")); // the administrator is not blocked
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100 && !await condition(); i++) await Task.Delay(50);
        Assert.True(await condition());
    }
}
