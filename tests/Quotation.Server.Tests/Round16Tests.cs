using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;

namespace Quotation.Server.Tests;

/// <summary>PDF file name order per user; removed themes fall back to Default Light.</summary>
public class Round16Tests : QuotationTestBase
{
    [Fact]
    public async Task File_name_order_is_saved_with_the_login()
    {
        Assert.Equal(PdfFileOrders.NumberFirst, (await Api.MeAsync()).PdfFileOrder);
        var me = await Api.SetMyPreferencesAsync(new MyPreferencesDto(PdfFileOrder: PdfFileOrders.CustomerFirst));
        Assert.Equal(PdfFileOrders.CustomerFirst, me.PdfFileOrder);
        Assert.Equal(PdfFileOrders.CustomerFirst, (await Api.MeAsync()).PdfFileOrder);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() =>
            Api.SetMyPreferencesAsync(new MyPreferencesDto(PdfFileOrder: "Sideways")))).Status);
    }

    [Fact]
    public async Task Removed_themes_become_default_light()
    {
        Assert.DoesNotContain("Lagoon", AppTheme.All);
        Assert.DoesNotContain("Cyberpunk", AppTheme.All);
        Assert.DoesNotContain("Glass", AppTheme.All);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() =>
            Api.SetMyPreferencesAsync(new MyPreferencesDto(Theme: "Cyberpunk")))).Status);
        Server.Db(db =>
        {
            foreach (var u in db.Users) u.Theme = "Lagoon"; // chosen before this version
            return db.SaveChanges();
        });
        Assert.Equal(AppTheme.Light, (await Api.MeAsync()).Theme);
    }
}
