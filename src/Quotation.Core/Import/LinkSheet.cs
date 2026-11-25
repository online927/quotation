using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Quotation.Core.Import;

/// <summary>
/// Round 38 (PL-1): reads the administrator's product links file — an Excel workbook (.xlsx, first sheet) or a CSV —
/// as (product code, web link) rows: column A = product code, column B = link. A heading row is skipped. When column B
/// holds a hyperlink with other words shown, the hyperlink's address is used.
/// </summary>
public static class LinkSheet
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<(string Code, string Url)> Read(byte[] file, string fileName)
    {
        var rows = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || !LooksLikeZip(file)
            ? ReadCsv(Encoding.UTF8.GetString(file))
            : ReadXlsx(file);
        // A heading row ("Code", "Link", "URL" …) is not a product.
        if (rows.Count > 0 && !LooksLikeLink(rows[0].Url)) rows.RemoveAt(0);
        return rows.Where(r => r.Code.Length > 0 || r.Url.Length > 0).ToList();
    }

    private static bool LooksLikeZip(byte[] b) => b.Length > 4 && b[0] == 'P' && b[1] == 'K';

    private static bool LooksLikeLink(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static List<(string Code, string Url)> ReadCsv(string text)
    {
        var rows = new List<(string Code, string Url)>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Trim().Length == 0) continue;
            var sep = raw.Contains('\t') ? '\t' : raw.Contains(';') && !raw.Contains(',') ? ';' : ',';
            var cells = SplitCsv(raw, sep);
            rows.Add((cells.ElementAtOrDefault(0)?.Trim() ?? "", cells.ElementAtOrDefault(1)?.Trim() ?? ""));
        }
        return rows;
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == sep && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    private static List<(string Code, string Url)> ReadXlsx(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        XDocument? Load(string path) => zip.GetEntry(path) is { } e ? XDocument.Load(e.Open()) : null;

        var shared = Load("xl/sharedStrings.xml")?.Root?.Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList() ?? [];

        // The first sheet of the workbook (by its relationship), else sheet1.xml.
        var sheetPath = "xl/worksheets/sheet1.xml";
        var firstSheet = Load("xl/workbook.xml")?.Root?.Element(Main + "sheets")?.Elements(Main + "sheet").FirstOrDefault();
        var relId = firstSheet?.Attribute(Rel + "id")?.Value;
        if (relId is not null && Load("xl/_rels/workbook.xml.rels")?.Root?.Elements(PackageRel + "Relationship")
                .FirstOrDefault(r => r.Attribute("Id")?.Value == relId)?.Attribute("Target")?.Value is { } target)
            sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        var sheet = Load(sheetPath) ?? throw new InvalidDataException("The Excel file has no sheet.");

        // Hyperlinks: cell reference → address (when the cell shows other words).
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relsPath = Path.GetDirectoryName(sheetPath)!.Replace('\\', '/') + "/_rels/" + Path.GetFileName(sheetPath) + ".rels";
        var rels = Load(relsPath)?.Root?.Elements(PackageRel + "Relationship")
            .ToDictionary(r => r.Attribute("Id")?.Value ?? "", r => r.Attribute("Target")?.Value ?? "") ?? [];
        foreach (var h in sheet.Root?.Element(Main + "hyperlinks")?.Elements(Main + "hyperlink") ?? [])
        {
            if (h.Attribute("ref")?.Value is { } cellRef && h.Attribute(Rel + "id")?.Value is { } id && rels.TryGetValue(id, out var url))
                links[cellRef.Split(':')[0]] = url;
        }

        var rows = new List<(string Code, string Url)>();
        foreach (var row in sheet.Root?.Element(Main + "sheetData")?.Elements(Main + "row") ?? [])
        {
            string a = "", b = "";
            foreach (var c in row.Elements(Main + "c"))
            {
                var cellRef = c.Attribute("r")?.Value ?? "";
                var column = new string(cellRef.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
                if (column is not ("A" or "B")) continue;
                var value = CellText(c, shared);
                if (column == "B" && links.TryGetValue(cellRef, out var link) && !LooksLikeLink(value)) value = link;
                if (column == "A") a = value.Trim(); else b = value.Trim();
            }
            rows.Add((a, b));
        }
        return rows;
    }

    private static string CellText(XElement c, List<string> shared)
    {
        var type = c.Attribute("t")?.Value;
        if (type == "inlineStr") return string.Concat(c.Descendants(Main + "t").Select(t => t.Value));
        var v = c.Element(Main + "v")?.Value ?? "";
        return type == "s" && int.TryParse(v, out var i) && i >= 0 && i < shared.Count ? shared[i] : v;
    }
}
