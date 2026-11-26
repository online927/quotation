using Quotation.Core.Import;
using Quotation.Core.Search;

namespace Quotation.Core.Tests;

/// <summary>Round 39 (DX-1): a description column copied from Excel, and a description column in pasted rows.</summary>
public class Round39Tests
{
    [Fact]
    public void Excel_cells_keep_their_alt_enter_lines_and_empty_cells()
    {
        var text = "Brand: Mitutoyo\r\n\"Range 0-300 mm\nWith case\"\r\n\r\nMade in Japan\r\n";
        Assert.Equal(["Brand: Mitutoyo", "Range 0-300 mm\nWith case", "", "Made in Japan"], ExcelColumnText.Cells(text));
    }

    [Fact]
    public void Only_the_first_column_of_each_row_is_kept_and_doubled_quotes_are_one()
    {
        Assert.Equal(["6\" caliper", "B"], ExcelColumnText.Cells("\"6\"\" caliper\"\tx\r\nB\ty\r\n"));
    }

    [Fact]
    public void A_description_column_beside_the_name_fills_each_lines_description()
    {
        var rows = "Item\tDescription\tQty\n187-901-10 bevel protractor\tWith magnifier\t2\nDigital caliper\tCarbide jaws\t1";
        var roles = PastedListParser.GuessColumns(rows, out var header);
        Assert.True(header);
        Assert.Equal([PastedColumnRole.Item, PastedColumnRole.Description, PastedColumnRole.Quantity], roles);
        var lines = PastedListParser.ParseTable(rows, roles, header);
        Assert.Equal(["With magnifier", "Carbide jaws"], lines.Select(l => l.Description));
        Assert.Equal("187-901-10 bevel protractor", lines[0].Query);
    }
}
