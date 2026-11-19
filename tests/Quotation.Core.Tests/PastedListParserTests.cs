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

/// <summary>Round 33: rows copied from Excel are read column by column, with their rates and discounts.</summary>
public class PastedExcelRowsTests
{
    [Fact]
    public void Part_numbers_with_a_quantity_column_keep_their_quantity()
    {
        // Before Round 33 these came in with quantity 1 and the number stayed in the search words
        var lines = PastedListParser.Parse("187-901-10\t2\n530-104\t5\n2046S\t3");
        Assert.Equal(["187-901-10", "530-104", "2046S"], lines.Select(l => l.Query));
        Assert.Equal([2m, 5m, 3m], lines.Select(l => l.Quantity));
        Assert.Equal(2m, PastedListParser.ParseLine("187-901-10\t2")!.Quantity); // one row on its own too
    }

    [Fact]
    public void Rates_and_discounts_are_read_from_their_columns()
    {
        const string rows = "187-901-10\tUniversal Bevel Protractor\t2\t15200\n530-104\tVernier caliper 0-150mm\t5\t2450\t5%\n" +
                            "2046S\tDial indicator 0-10mm\t3\t3800\n\t3 core 2.5 sqmm flexible cable\t100\t42.50";
        Assert.Equal([PastedColumnRole.PartNo, PastedColumnRole.Item, PastedColumnRole.Quantity, PastedColumnRole.Rate, PastedColumnRole.Discount],
            PastedListParser.GuessColumns(rows, out var header));
        Assert.False(header);
        var lines = PastedListParser.Parse(rows);
        Assert.Equal(["187-901-10", "530-104", "2046S", "3 core 2.5 sqmm flexible cable"], lines.Select(l => l.Query));
        Assert.Equal([2m, 5m, 3m, 100m], lines.Select(l => l.Quantity));
        Assert.Equal([15200m, 2450m, 3800m, 42.50m], lines.Select(l => l.Rate));
        Assert.Equal([null, 5m, null, null], lines.Select(l => l.Discount));
        Assert.Equal("Universal Bevel Protractor", lines[0].NameQuery); // searched when the part number finds nothing
    }

    [Fact]
    public void A_heading_row_names_the_columns_and_the_amount_is_left_out()
    {
        const string rows = "Sl\tPart No\tDescription\tQty\tRate\tAmount\n1\t187-901-10\tBevel protractor\t2\t\"15,600.00\"\t\"31,200.00\"\n" +
                            "2\t530-104\tVernier caliper\t5\t\"2,450.00\"\t\"12,250.00\"";
        Assert.Equal([PastedColumnRole.Skip, PastedColumnRole.PartNo, PastedColumnRole.Item, PastedColumnRole.Quantity, PastedColumnRole.Rate, PastedColumnRole.Skip],
            PastedListParser.GuessColumns(rows, out var header));
        Assert.True(header);
        var lines = PastedListParser.Parse(rows);
        Assert.Equal(2, lines.Count);
        Assert.Equal([15600m, 2450m], lines.Select(l => l.Rate));
    }

    [Fact]
    public void Without_a_heading_the_serial_number_and_the_amount_are_left_out()
    {
        var roles = PastedListParser.GuessColumns("1\t187-901-10\t2\t15600\t31200\n2\t530-104\t5\t2450\t12250", out _);
        Assert.Equal([PastedColumnRole.Skip, PastedColumnRole.PartNo, PastedColumnRole.Quantity, PastedColumnRole.Rate, PastedColumnRole.Skip], roles);
        // One column of big numbers is a rate (the quantity is then 1)
        var lines = PastedListParser.Parse("187-901-10\t15600\n530-104\t2450");
        Assert.Equal([15600m, 2450m], lines.Select(l => l.Rate));
        Assert.Equal([1m, 1m], lines.Select(l => l.Quantity));
    }

    [Fact]
    public void Columns_can_be_set_by_hand()
    {
        var lines = PastedListParser.ParseTable("187-901-10\t2\t15600\n530-104\t5\t2450",
            [PastedColumnRole.PartNo, PastedColumnRole.Skip, PastedColumnRole.Rate], hasHeader: false);
        Assert.Equal([1m, 1m], lines.Select(l => l.Quantity));
        Assert.Equal([15600m, 2450m], lines.Select(l => l.Rate));
    }
}
