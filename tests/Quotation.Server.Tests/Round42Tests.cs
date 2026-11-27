using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 42: search by amount, quantity and more fields (SR-1); the dashboard's numbers (DB-1).</summary>
public class Round42Tests : QuotationTestBase
{
    [Fact]
    public async Task History_finds_a_quotation_by_its_amounts_quantity_and_more_fields()
    {
        var request = await BevelRequest(qty: 3);
        request.Buyer.Email = "purchase@sonepar.example";
        request.DispatchedThrough = "Blue Dart courier";
        request.Destination = "Chakan warehouse";
        request.ConsigneeSameAsBuyer = false;
        request.Consignee = new PartyDto { Name = "Sonepar Warehouse", Address = "Gat No. 315, Nighoje", Gstin = "27AAACS9999F1Z1", ContactPerson = "Sunil Patil", Phone = "9822067890" };
        var q = await Api.CreateQuotationAsync(request);
        var other = await Api.CreateQuotationAsync(await BevelRequest(qty: 1));
        async Task<bool> Finds(string text) => (await Api.QuotationsAsync(text)).Any(s => s.Id == q.Id);

        var total = q.GrandTotal;
        var whole = decimal.Truncate(total);
        Assert.True(await Finds(whole.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));         // "36816"
        Assert.True(await Finds(IndianFormat.Amount(total)));                                                      // "36,816.00"
        Assert.True(await Finds("₹" + IndianFormat.Amount(total, 0)));                                             // "₹36,816"
        Assert.True(await Finds(q.Lines[0].Rate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))); // the rate
        Assert.True(await Finds(q.Subtotal.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));     // before tax
        Assert.DoesNotContain(await Api.QuotationsAsync(whole.ToString("0", System.Globalization.CultureInfo.InvariantCulture)), s => s.Id == other.Id);

        Assert.True(await Finds("qty 3"));
        Assert.True(await Finds("x3"));
        Assert.DoesNotContain(await Api.QuotationsAsync("qty 3"), s => s.Id == other.Id);

        foreach (var word in new[] { "purchase@sonepar", "Nighoje", "27AAACS9999F1Z1", "Sunil", "9822067890", "Blue Dart", "Chakan warehouse" })
            Assert.True(await Finds(word), word);
    }

    [Fact]
    public async Task Dashboard_numbers_count_approved_quotations_by_value_customer_item_and_user()
    {
        var all = await Api.SettingsAsync();
        all.Quotation.TrackOutcomes = true;
        await Api.SaveSettingsAsync(all);
        var a = await Api.CreateQuotationAsync(await BevelRequest(qty: 2));
        await Api.ApproveQuotationAsync(a.Id);
        var b = await Api.CreateQuotationAsync(await BevelRequest(qty: 5));
        await Api.ApproveQuotationAsync(b.Id);
        await Api.CreateQuotationAsync(await BevelRequest(qty: 9)); // a draft: not counted

        var month = await Api.DashboardStatsAsync("month");
        Assert.Equal(2, month.Count);
        var approvedA = await Api.QuotationAsync(a.Id);
        var approvedB = await Api.QuotationAsync(b.Id);
        Assert.Equal(approvedA.GrandTotal + approvedB.GrandTotal, month.QuotedValue);
        Assert.Equal(approvedB.Id, month.Biggest!.Id);
        var top = Assert.Single(month.CustomersByValue);
        Assert.Equal(2, top.Count);
        var item = Assert.Single(month.Items);
        Assert.Equal(2, item.Times);
        var me = Assert.Single(month.Team);
        Assert.True(me.Me);
        Assert.Equal(1, me.Rank);
        Assert.NotNull(month.Outcomes);
        Assert.Equal(2, month.Outcomes!.Open);
        Assert.Equal(12, month.ValueTrend.Count);
        Assert.Contains(month.Activity, d => d.Date == Today && d.Count == 2);
        Assert.True(month.Activity.Count >= 7 * 16);

        var today = await Api.DashboardStatsAsync("today");
        Assert.Equal(2, today.Count);
        Assert.Equal(0, today.PreviousCount);
        Assert.Equal("month", (await Api.DashboardStatsAsync("nonsense")).Period);

        // A standard user sees only their own place in the team
        await Api.CreateUserAsync(new CreateUserRequest("ravi42", "Ravi", "first-pass", UserRole.User));
        var ravi = Server.CreateApi("USER-PC");
        await ravi.LoginAsync("ravi42", "first-pass");
        await ravi.ChangePasswordAsync("first-pass", "user-pass");
        Assert.Empty((await ravi.DashboardStatsAsync()).Team);
    }
}
