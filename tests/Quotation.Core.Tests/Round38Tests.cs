using System.IO.Compression;
using System.Text;
using Quotation.Core.Domain;
using Quotation.Core.Import;
using Quotation.Core.Search;

namespace Quotation.Core.Tests;

/// <summary>Round 38: delivery lines are never remembered (DM-1), the product links file (PL-1), joined-word typos (Q7).</summary>
public class Round38Tests
{
    [Theory]
    [InlineData("Brand: Mitutoyo\nDelivery: 2-3 weeks", "Brand: Mitutoyo")]
    [InlineData("Delivery time - 1 week\nRange 0-25 mm", "Range 0-25 mm")]
    [InlineData("Del. ex stock\nEx-stock\nex stock", "")]
    [InlineData("DELIVERY 4-6 WEEKS", "")]
    [InlineData("Range 0-360°\nDelivered calibrated", "Range 0-360°\nDelivered calibrated")]
    [InlineData("Free delivery in Bangalore", "Free delivery in Bangalore")]
    public void Delivery_lines_are_left_out_of_a_remembered_description(string description, string remembered) =>
        Assert.Equal(remembered, DeliveryText.WithoutDeliveryLines(description));

    [Fact]
    public void Links_are_read_from_a_csv_with_a_heading_row()
    {
        var rows = LinkSheet.Read(Encoding.UTF8.GetBytes("Code,Link\n187-901-10,https://a.example/bevel\n\"112,201\",www.b.example/mic\n"), "links.csv");
        Assert.Equal([("187-901-10", "https://a.example/bevel"), ("112,201", "www.b.example/mic")], rows);
    }

    [Fact]
    public void Links_are_read_from_an_excel_workbook_with_shared_text_and_hyperlinks()
    {
        var rows = LinkSheet.Read(Workbook(), "links.xlsx");
        Assert.Equal([("187-901-10", "https://a.example/bevel"), ("530-104", "https://b.example/caliper")], rows);
    }

    /// <summary>A minimal .xlsx: heading row, a plain link and a cell showing "Open" with a hyperlink behind it.</summary>
    private static byte[] Workbook()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open());
                w.Write(xml);
            }
            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            const string pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
            Add("xl/workbook.xml", $"<workbook xmlns=\"{main}\" xmlns:r=\"{rel}\"><sheets><sheet name=\"Links\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{pkg}\"><Relationship Id=\"rId1\" Type=\"ws\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/sharedStrings.xml", $"<sst xmlns=\"{main}\"><si><t>Product code</t></si><si><t>Web link</t></si><si><t>187-901-10</t></si><si><t>Open</t></si></sst>");
            Add("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{main}\" xmlns:r=\"{rel}\"><sheetData>"
                + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\" t=\"inlineStr\"><is><t>https://a.example/bevel</t></is></c></row>"
                + "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>530-104</t></is></c><c r=\"B3\" t=\"s\"><v>3</v></c></row>"
                + "</sheetData><hyperlinks><hyperlink ref=\"B3\" r:id=\"rId7\"/></hyperlinks></worksheet>");
            Add("xl/worksheets/_rels/sheet1.xml.rels", $"<Relationships xmlns=\"{pkg}\"><Relationship Id=\"rId7\" Type=\"hyperlink\" Target=\"https://b.example/caliper\" TargetMode=\"External\"/></Relationships>");
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData("borgauge", "BORE GAUGE 18-35MM")]
    [InlineData("micrometr 0-25", "103-137 MICROMETER OUTSIDE 0-25MM")]
    [InlineData("protracter", "187-901-10-UNIVERSAL BEVEL PROTRACTOR")]
    [InlineData("dial guage", "DIAL GAUGE 0-10MM")]
    public void Spelling_mistakes_and_joined_words_still_find_the_item(string typed, string expected)
    {
        string[] names = ["103-137 MICROMETER OUTSIDE 0-25MM", "KEI DIGITAL CALIPER 0-300MM", "187-901-10-UNIVERSAL BEVEL PROTRACTOR",
            "DIAL GAUGE 0-10MM", "BORE GAUGE 18-35MM", "DIAL INDICATOR STAND"];
        var index = new SearchIndex<string>(names, [new SearchField<string>("Name", s => s, 1.0, IsPrimaryName: true)]);
        Assert.Contains(expected, index.Search(typed, 5).Select(h => h.Item));
    }
}
