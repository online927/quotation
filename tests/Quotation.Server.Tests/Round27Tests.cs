using Quotation.ApiClient;
using Quotation.Contracts;

namespace Quotation.Server.Tests;

/// <summary>Round 27: Formater matches remembered for every PC, the Ocean / Midnight Stars themes and the Greetings switch.</summary>
public class Round27Tests : QuotationTestBase
{
    [Fact]
    public async Task Formater_matches_are_remembered_for_everyone_and_can_be_forgotten()
    {
        var ravi = await Server.LoginAsStandardAsync();
        var customer = (await ravi.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await ravi.SearchProductsAsync("universal bevel")).First().Product;

        await ravi.RememberFormaterMatchAsync(new FormaterMatchDto(FormaterMatchDto.Customer, "  Sonepar   PUNE ", customer.Id));
        await ravi.RememberFormaterMatchAsync(new FormaterMatchDto(FormaterMatchDto.Product, "bevel 150", product.Id));
        // Chosen again for the same typing: replaced, not doubled.
        await ravi.RememberFormaterMatchAsync(new FormaterMatchDto(FormaterMatchDto.Product, "Bevel 150", product.Id));

        // Another user (another PC) sees the same, with the Tally names.
        var all = await Api.FormaterMatchesAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m is { Kind: FormaterMatchDto.Customer, Typed: "sonepar pune" } && m.TallyId == customer.Id && m.TallyName == customer.Name);
        Assert.Contains(all, m => m is { Kind: FormaterMatchDto.Product, Typed: "bevel 150" } && m.TallyName == product.Name);

        // Only real Tally entries.
        var bad = await Assert.ThrowsAsync<ApiException>(() => ravi.RememberFormaterMatchAsync(new FormaterMatchDto(FormaterMatchDto.Product, "x", 999_999)));
        Assert.Equal(ApiErrorCodes.Validation, bad.Code);

        await ravi.ForgetFormaterMatchAsync(FormaterMatchDto.Customer, "Sonepar Pune");
        Assert.Single(await ravi.FormaterMatchesAsync());
    }

    [Fact]
    public async Task Ocean_and_midnight_stars_themes_are_saved_and_greetings_are_an_administrator_switch()
    {
        var ravi = await Server.LoginAsStandardAsync();
        Assert.Equal(AppTheme.Ocean, (await ravi.SetMyPreferencesAsync(new MyPreferencesDto(Theme: AppTheme.Ocean))).Theme);
        Assert.Equal(AppTheme.Midnight, (await ravi.SetMyPreferencesAsync(new MyPreferencesDto(Theme: AppTheme.Midnight))).Theme);
        Assert.True(AppTheme.IsDarkTheme(AppTheme.Midnight));
        Assert.False(AppTheme.IsDarkTheme(AppTheme.Ocean));

        var all = await Api.SettingsAsync();
        Assert.False(all.Quotation.Greetings);
        all.Quotation.Greetings = true;
        await Api.SaveSettingsAsync(all);
        Assert.True((await ravi.SettingsAsync()).Quotation.Greetings); // every user's app reads it
        var settings = await ravi.SettingsAsync();
        await Assert.ThrowsAsync<ApiException>(() => ravi.SaveSettingsAsync(settings)); // only the administrator changes it
    }
}
