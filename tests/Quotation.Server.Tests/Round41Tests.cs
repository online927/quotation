using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>Round 41 (SL-2): the Zoho seal starts at the bottom right and moves up to 450 points either way.</summary>
public class Round41Tests : QuotationTestBase
{
    [Fact]
    public async Task The_seal_can_move_450_points_and_an_earlier_zoho_place_starts_again_at_the_bottom_right()
    {
        await Api.SaveSealPositionAsync(new SealPositionDto(100, 420, -430, PdfLayouts.Zoho));
        Assert.Equal((100d, 420d, -430d), (await Api.SettingsAsync()).Company.ZohoSeal());
        await Assert.ThrowsAsync<ApiException>(() => Api.SaveSealPositionAsync(new SealPositionDto(100, 460, 0, PdfLayouts.Zoho)));

        // A server updated from Round 40: the Zoho seal moved then starts at the new standard place (offsets 0)
        Server.Db(db => db.Settings.Where(s => s.Key == SettingsService.UpgradeRound41Key).ExecuteDelete());
        var fresh = new SettingsService(Server.Services.GetRequiredService<IServiceScopeFactory>(),
            Server.Services.GetRequiredService<Quotation.Server.Infrastructure.ISecretProtector>(), TimeProvider.System);
        await fresh.ApplyUpgradesAsync(CancellationToken.None);
        Assert.Equal((100d, 0d, 0d), fresh.Company.ZohoSeal());
    }
}
