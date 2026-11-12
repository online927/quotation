using Quotation.Core.Search;

namespace Quotation.Core.Tests;

public class PastedListParserTests
{
    [Theory]
    [InlineData("187-901 x 2", "187-901", 2)]
    [InlineData("187-901x2", "187-901", 2)]
    [InlineData("530-312 - 5 nos", "530-312", 5)]
    [InlineData("530-312 5nos", "530-312", 5)]
    [InlineData("mitutoyo 2046s qty 3", "mitutoyo 2046s", 3)]
    [InlineData("mitutoyo 2046s - qty: 3 pcs", "mitutoyo 2046s", 3)]
    [InlineData("digital caliper 200mm 1", "digital caliper 200mm", 1)]
    [InlineData("Dial gauge : 4", "Dial gauge", 4)]
    [InlineData("2 x 187-901-10", "187-901-10", 2)]
    [InlineData("3 nos vernier caliper 150mm", "vernier caliper 150mm", 3)]
    [InlineData("1. 187-901 x 2", "187-901", 2)]
    [InlineData("• 530-312 - 5 nos", "530-312", 5)]
    [InlineData("[11/11, 10:15 am] Ravi: 187-901 x 2", "187-901", 2)]
    [InlineData("1\tVernier caliper 150mm\t2\tNos", "Vernier caliper 150mm", 2)]
    [InlineData("Pls quote 187-901 x 2", "Pls quote 187-901", 2)]
    public void Reads_the_item_and_its_quantity(string line, string query, decimal quantity)
    {
        var parsed = PastedListParser.ParseLine(line)!;
        Assert.Equal(query, parsed.Query);
        Assert.Equal(quantity, parsed.Quantity);
        Assert.True(parsed.QuantityGiven);
    }

    [Theory]
    [InlineData("hex key set metric", "hex key set metric")]
    [InlineData("187-901-10", "187-901-10")]
    [InlineData("tap M10 x 1.25", "tap M10 x 1.25")]       // thread size, not a quantity
    [InlineData("gauge max", "gauge max")]
    [InlineData("caliper size 150", "caliper size 150")]   // a size, not a quantity
    [InlineData("500-197-30", "500-197-30")]
    public void No_quantity_means_one(string line, string query)
    {
        var parsed = PastedListParser.ParseLine(line)!;
        Assert.Equal(query, parsed.Query);
        Assert.Equal(1, parsed.Quantity);
        Assert.False(parsed.QuantityGiven);
    }

    [Fact]
    public void Greetings_signatures_and_empty_lines_are_skipped()
    {
        var text = """
            Dear Sir,
            Please quote for the following items:

            1. 187-901 x 2
            2. 530-312 - 5 nos
            3. hex key set metric

            Thanks & Regards
            +91 98220 12345
            """;
        var lines = PastedListParser.Parse(text);
        Assert.Equal(["187-901", "530-312", "hex key set metric"], lines.Select(l => l.Query));
        Assert.Equal([2m, 5m, 1m], lines.Select(l => l.Quantity));
        Assert.True(PastedListParser.LooksLikeAList(text));
    }

    [Fact]
    public void One_line_is_not_a_list()
    {
        Assert.False(PastedListParser.LooksLikeAList("187-901-10-UNIVERSAL BEVEL PROTRACTOR"));
        Assert.False(PastedListParser.LooksLikeAList("Dear sir\n187-901 x 2\nThanks"));
        Assert.True(PastedListParser.LooksLikeAList("187-901 x 2\r\n530-312 x 1"));
    }
}
