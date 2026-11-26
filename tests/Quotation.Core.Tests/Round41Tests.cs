using Quotation.Core.Search;

namespace Quotation.Core.Tests;

/// <summary>Round 41 (PD-2): a "Delivery" / "Lead time" column of pasted Excel rows.</summary>
public class Round41Tests
{
    [Fact]
    public void A_delivery_column_is_read_from_pasted_rows()
    {
        const string text = "Part No\tQty\tDelivery\n187-901-10\t2\tEx-stock\n530-104\t1\t2-3 weeks\n";
        var roles = PastedListParser.GuessColumns(text, out var header);
        Assert.True(header);
        Assert.Equal(PastedColumnRole.Delivery, roles[2]);
        var lines = PastedListParser.ParseTable(text, roles, header);
        Assert.Equal(["Ex-stock", "2-3 weeks"], lines.Select(l => l.Delivery));
        Assert.Equal(PastedColumnRole.Delivery, PastedListParser.HeaderRole("Lead time"));
        Assert.Equal(PastedColumnRole.Delivery, PastedListParser.HeaderRole("Del"));
        Assert.Equal(PastedColumnRole.Discount, PastedListParser.HeaderRole("Disc %"));
    }
}
