using System.Globalization;
using System.Text.RegularExpressions;

namespace Quotation.Core.Search;

/// <summary>One item line of a pasted list: what was pasted, the words to search for and the quantity.</summary>
public sealed record PastedLine(string Raw, string Query, decimal Quantity, bool QuantityGiven)
{
    /// <summary>The rate from an Excel row (null: the item's Tally rate).</summary>
    public decimal? Rate { get; init; }
    /// <summary>The discount % from an Excel row.</summary>
    public decimal? Discount { get; init; }
    /// <summary>The item name of an Excel row whose search words are its part number (searched when the part number finds nothing).</summary>
    public string? NameQuery { get; init; }
    /// <summary>Round 39 (DX-1): the description from an Excel row's description column (null: the item's own).</summary>
    public string? Description { get; init; }
    /// <summary>Round 41 (PD-2): the delivery from an Excel row's delivery column ("Ex-stock", "2-3 weeks").</summary>
    public string? Delivery { get; init; }
}

/// <summary>What a column of rows copied from Excel holds.</summary>
public enum PastedColumnRole { Skip, Item, PartNo, Quantity, Rate, Discount, Description, Delivery }

/// <summary>
/// Reads a list copied from WhatsApp, e-mail or Excel ("187-901 x 2", "530-312 - 5 nos", "qty 3 dial gauge")
/// into item lines. Greetings and empty lines are skipped; a line without a quantity counts as 1.
/// </summary>
public static partial class PastedListParser
{
    public const int MaxLines = 100;

    private const string Units = "nos|no|nos\\.|no\\.|pcs|pc|pieces|piece|units|unit|sets|set|ea|each|numbers|qty|pairs|pair|pkt|pkts|box|boxes";

    // WhatsApp copy: "[11/11, 10:15 am] Ravi: 187-901 x 2"
    [GeneratedRegex(@"^\[[^\]]{3,40}\]\s*[^:]{1,40}:\s*")]
    private static partial Regex WhatsAppPrefix();

    // List numbering and bullets: "1. ", "2) ", "- ", "• ", "* ", "a) "
    [GeneratedRegex(@"^\s*(?:\d{1,3}[.)]\s+|[a-zA-Z][.)]\s+|[-•*·>]+\s*)")]
    private static partial Regex Bullet();

    // "... x 2", "... x2", "... * 2", "... × 2" at the end (optionally followed by a unit); whole numbers only, so a
    // thread size such as "M10 x 1.25" is not read as a quantity
    [GeneratedRegex(@"(?:\s+|(?<=\d))[x×*]\s*(?<q>\d+)\s*(?:" + Units + @")?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TimesAtEnd();

    // "... qty 2", "... qty: 2", "... quantity - 2", "... qty 2 nos" at the end
    [GeneratedRegex(@"[\s,;\-]*\b(?:qty|quantity|qnty|qnt)\b\s*[:=\-]?\s*(?<q>\d+(?:\.\d+)?)\s*(?:" + Units + @")?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex QtyWordAtEnd();

    // "... 2 nos", "... - 5 nos", "... 3pcs", "... : 4 sets" at the end ("187-901-10pcs" keeps its part number)
    [GeneratedRegex(@"(?:\s*[,;:=]\s*|\s+-?\s*|-\s+)(?<q>\d+(?:\.\d+)?)\s*(?:" + Units + @")\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberUnitAtEnd();

    // "... - 2", "... : 2", "... = 2", "..., 2" at the end (a dash needs a space next to it: "187-901-10" is a part number)
    [GeneratedRegex(@"(?:\s*[:=,;]\s*|\s+-\s*|-\s+)(?<q>\d+(?:\.\d+)?)\s*$")]
    private static partial Regex SeparatorNumberAtEnd();

    // "... 2" at the end (a plain whole number after a space)
    [GeneratedRegex(@"\s+(?<q>\d{1,4})\s*$")]
    private static partial Regex PlainNumberAtEnd();

    // "2 x ...", "2 nos ...", "qty 2 ...", "2 - ..." at the start
    [GeneratedRegex(@"^(?:(?:qty|quantity)\s*[:=\-]?\s*)?(?<q>\d+(?:\.\d+)?)\s*(?:[x×*]|(?:" + Units + @")\.?|-|:)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityAtStart();

    // Phone numbers (10 or more digits and nothing else); "187-901-10" is a part number.
    [GeneratedRegex(@"^[+()\d\s\-]{8,}$")]
    private static partial Regex PhoneLike();

    // Lines that are not items (only when they hold no number: "Pls quote 187-901 x 2" is an item).
    [GeneratedRegex(@"^(?:dear|hi|hello|hey|good\s+(?:morning|afternoon|evening)|greetings|please|pls|plz|kindly|request|need\s+(?:quotation|quote|rates?|price)|send\s+(?:quotation|quote)|quotation\s+(?:for|required)|thanks?|thank\s+you|thx|regards|best\s+regards|warm\s+regards|sir|madam|noted)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAnItem();

    /// <summary>True when the text has two or more item lines (a single line is typed into the box as usual).</summary>
    public static bool LooksLikeAList(string? text) => Parse(text).Count >= 2;

    public static List<PastedLine> Parse(string? text)
    {
        var result = new List<PastedLine>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        // Rows copied from Excel: read column by column (part no., name, qty, rate, disc. %)
        if (IsTable(text)) return ParseTable(text, GuessColumns(text, out var header), header);
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (result.Count >= MaxLines) break;
            var line = ParseLine(rawLine);
            if (line is not null) result.Add(line);
        }
        return result;
    }

    public static PastedLine? ParseLine(string rawLine)
    {
        var raw = rawLine.Trim();
        if (raw.Length == 0 || !raw.Any(char.IsLetterOrDigit)) return null;

        var s = WhatsAppPrefix().Replace(raw, "");
        // Excel rows: columns separated by tabs; a first column holding only a serial number is dropped.
        if (s.Contains('\t'))
        {
            var cells = s.Split('\t').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (cells.Count > 1 && int.TryParse(cells[0], out _)) cells.RemoveAt(0);
            // "187-901-10 <tab> 2": a last cell that is a whole number is the quantity
            if (cells.Count > 1 && int.TryParse(cells[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var q) && q > 0)
            {
                var rest = string.Join(' ', cells.Take(cells.Count - 1)).Trim();
                if (rest.Any(char.IsLetterOrDigit)) return new PastedLine(raw, rest, q, true);
            }
            s = string.Join(' ', cells);
        }
        s = Bullet().Replace(s, "").Trim();
        if (s.Length == 0 || (PhoneLike().IsMatch(s) && s.Count(char.IsDigit) >= 10)) return null;
        if (!s.Any(char.IsDigit) && NotAnItem().IsMatch(s)) return null;

        decimal? quantity = null;
        foreach (var rule in new[] { TimesAtEnd(), QtyWordAtEnd(), NumberUnitAtEnd(), SeparatorNumberAtEnd() })
        {
            if (TryTake(rule, ref s, out var q)) { quantity = q; break; }
        }
        if (quantity is null && TryTakeAtStart(ref s, out var first)) quantity = first;
        // A plain number at the end, only when the rest still has words of its own ("digital caliper 200mm 1").
        if (quantity is null)
        {
            var m = PlainNumberAtEnd().Match(s);
            if (m.Success && s[..m.Index].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2 &&
                !EndsWithSizeWord(s[..m.Index]))
            {
                quantity = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
                s = s[..m.Index];
            }
        }

        var query = s.Trim(' ', ',', ';', '-', ':', '.', '=');
        if (!query.Any(char.IsLetterOrDigit)) return null;
        if (quantity is <= 0) quantity = null;
        return new PastedLine(raw, query, quantity ?? 1, quantity is not null);
    }

    private static bool TryTake(Regex rule, ref string s, out decimal quantity)
    {
        quantity = 0;
        var m = rule.Match(s);
        if (!m.Success || m.Index == 0) return false; // nothing left to search for
        quantity = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
        s = s[..m.Index];
        return true;
    }

    private static bool TryTakeAtStart(ref string s, out decimal quantity)
    {
        quantity = 0;
        var m = QuantityAtStart().Match(s);
        if (!m.Success || m.Length >= s.Length) return false;
        quantity = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
        s = s[m.Length..];
        return true;
    }

    /// <summary>"... size 150", "... range 300": the number belongs to the item, not the quantity.</summary>
    private static bool EndsWithSizeWord(string before)
    {
        var last = before.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant();
        return last is "x" or "×" or "*" or "size" or "range" or "dia" or "length" or "capacity" or "cap" or "model" or "no" or "no." or "sr" or "series" or "type" or "grade" or "class";
    }

    // ================================================================= rows copied from Excel

    /// <summary>Two or more rows with columns (cells separated by tabs, as Excel copies them).</summary>
    public static bool IsTable(string? text)
    {
        var rows = Rows(text);
        return rows.Count >= 2 && rows.Count(r => r.Contains('\t')) * 2 >= rows.Count;
    }

    private static List<string> Rows(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => l.Trim().Length > 0).ToList();

    private static List<string[]> Cells(string? text) =>
        Rows(text).Select(r => r.Split('\t').Select(c => c.Trim().Trim('"').Trim()).ToArray()).ToList();

    /// <summary>The columns of Excel rows: part no., name, qty, rate, disc. % (from a heading row when there is one, else from the values).</summary>
    public static PastedColumnRole[] GuessColumns(string? text, out bool hasHeader)
    {
        var rows = Cells(text);
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var roles = new PastedColumnRole?[width];
        hasHeader = false;
        if (rows.Count == 0) return [];

        // A heading row ("Part No | Description | Qty | Rate"): its words decide those columns
        var first = rows[0];
        var named = first.Select(HeaderRole).ToArray();
        if (named.Count(r => r is not null) >= 2 || (named.Any(r => r is not null) && !first.Any(c => TryNumber(c, out _, out _, out _))))
        {
            hasHeader = true;
            for (var c = 0; c < named.Length; c++) roles[c] = named[c];
            // Round 39 (DX-1): with a name column as well, a "Description" / "Specification" / "Details" column is the
            // line's description, not more name words.
            if (named.Count(r => r == PastedColumnRole.Item) >= 2)
                for (var c = 0; c < named.Length; c++)
                    if (named[c] == PastedColumnRole.Item && IsDescriptionHeading(first[c])) roles[c] = PastedColumnRole.Description;
        }
        var data = hasHeader ? rows.Skip(1).ToList() : rows;
        string Cell(string[] row, int c) => c < row.Length ? row[c] : "";

        var qtyLike = new List<int>();
        var moneyLike = new List<int>();
        for (var c = 0; c < width; c++)
        {
            if (roles[c] is not null) continue;
            var values = data.Select(r => Cell(r, c)).Where(v => v.Length > 0).ToList();
            if (values.Count == 0) { roles[c] = PastedColumnRole.Skip; continue; }
            var numbers = values.Select(v => (Ok: TryNumber(v, out var n, out var money, out var percent), n, money, percent, v)).ToList();
            if (numbers.All(x => x.Ok && x.percent)) { roles[c] = PastedColumnRole.Discount; continue; }
            if (numbers.All(x => x.Ok))
            {
                var whole = numbers.All(x => x.n == decimal.Truncate(x.n) && !x.v.Contains('.') && !x.money);
                var serial = whole && c == FirstFilledColumn(data) && numbers.Select((x, i) => x.n == i + 1).All(ok => ok);
                var hsnLike = whole && numbers.All(x => x.n >= 100000 && x.n < 100000000);
                if (serial || hsnLike) { roles[c] = PastedColumnRole.Skip; continue; }
                if (whole && numbers.Max(x => x.n) <= 100000) qtyLike.Add(c); else moneyLike.Add(c);
                continue;
            }
            var partLike = values.Count(v => v.Length <= 30 && !v.Contains(' ') && v.Any(char.IsDigit));
            roles[c] = partLike * 10 >= values.Count * 7 ? PastedColumnRole.PartNo : PastedColumnRole.Item;
        }

        // Numbers: the first whole-number column is the quantity, the money column (or the next one) the rate; a third
        // column of small numbers is the discount; anything else (an amount) is left out
        var hasQty = roles.Contains(PastedColumnRole.Quantity);
        var hasRate = roles.Contains(PastedColumnRole.Rate);
        var hasDisc = roles.Contains(PastedColumnRole.Discount);
        decimal Max(int c) => data.Select(r => TryNumber(Cell(r, c), out var n, out _, out _) ? n : 0).DefaultIfEmpty(0).Max();
        decimal Min(int c) => data.Where(r => Cell(r, c).Length > 0)
            .Select(r => TryNumber(Cell(r, c), out var n, out _, out _) ? n : 0).DefaultIfEmpty(0).Min();
        if (!hasQty && qtyLike.Count > 0)
        {
            // The column with the smallest whole numbers is the quantity (a lone column of big numbers is a rate)
            var q = qtyLike.OrderBy(Max).First();
            if (!(qtyLike.Count == 1 && moneyLike.Count == 0 && !hasRate && Min(q) >= 1000))
            {
                roles[q] = PastedColumnRole.Quantity;
                hasQty = true;
            }
        }
        // An amount column (qty × another column) is left out
        var qtyCol = Array.IndexOf(roles, PastedColumnRole.Quantity);
        if (qtyCol >= 0)
        {
            decimal? Val(string[] row, int c) => TryNumber(Cell(row, c), out var n, out _, out _) ? n : null;
            var rest = qtyLike.Concat(moneyLike).Where(c => roles[c] is null).ToList();
            foreach (var y in rest)
                foreach (var x in rest.Where(x => x != y))
                    if (data.All(r => Val(r, y) is not { } a || Val(r, x) is not { } b || Val(r, qtyCol) is not { } q || Math.Abs(a - q * b) <= a * 0.02m + 1))
                    {
                        roles[y] = PastedColumnRole.Skip;
                        break;
                    }
        }
        if (!hasRate)
        {
            // The money column, else the remaining column with the biggest numbers
            var r = moneyLike.Where(c => roles[c] is null).DefaultIfEmpty(-1).First();
            if (r < 0) r = qtyLike.Where(c => roles[c] is null).OrderByDescending(Max).DefaultIfEmpty(-1).First();
            if (r >= 0)
            {
                roles[r] = PastedColumnRole.Rate;
                hasRate = true;
            }
        }
        foreach (var c in qtyLike.Concat(moneyLike).Where(c => roles[c] is null).OrderBy(c => c))
        {
            if (!hasDisc && Max(c) <= 100)
            {
                roles[c] = PastedColumnRole.Discount;
                hasDisc = true;
                continue;
            }
            roles[c] = PastedColumnRole.Skip; // an amount (qty × rate)
        }

        var result = roles.Select(r => r ?? PastedColumnRole.Skip).ToArray();
        if (!result.Contains(PastedColumnRole.Item) && !result.Contains(PastedColumnRole.PartNo))
        {
            // No words column found: the first text column is the item
            for (var c = 0; c < width; c++)
                if (data.Any(r => Cell(r, c).Any(char.IsLetter))) { result[c] = PastedColumnRole.Item; break; }
        }
        return result;
    }

    private static int FirstFilledColumn(List<string[]> rows)
    {
        var width = rows.Max(r => r.Length);
        for (var c = 0; c < width; c++)
            if (rows.Any(r => c < r.Length && r[c].Length > 0)) return c;
        return 0;
    }

    /// <summary>A heading cell's column: "Qty", "Rate", "Disc %", "Part No", "Description", "Amount" …</summary>
    public static PastedColumnRole? HeaderRole(string cell)
    {
        var h = Regex.Replace(cell.ToLowerInvariant(), @"[^a-z%# ]", " ").Trim();
        h = Regex.Replace(h, @"\s+", " ");
        if (h.Length == 0) return null;
        if (h.Contains("disc") || h == "%" || h == "dis") return PastedColumnRole.Discount;
        // Round 41 (PD-2): "Delivery", "Del", "Lead time", "Delivery period"
        if (h.StartsWith("deliv") || h is "del" or "del period" || h.Contains("lead time") || h.Contains("leadtime")) return PastedColumnRole.Delivery;
        if (h.Contains("amount") || h.Contains("total") || h.Contains("value") || h is "amt" or "net") return PastedColumnRole.Skip;
        if (h is "qty" or "qnty" or "quantity" or "nos" or "pcs" || h.StartsWith("qty") || h.StartsWith("quantity")) return PastedColumnRole.Quantity;
        if (h.Contains("rate") || h.Contains("price") || h.Contains("mrp") || h == "basic") return PastedColumnRole.Rate;
        if (h.Contains("part") || h.Contains("code") || h.Contains("model") || h.StartsWith("cat")) return PastedColumnRole.PartNo;
        if (h.Contains("desc") || h.Contains("item") || h.Contains("name") || h.Contains("product") || h.Contains("particular") || h.Contains("material"))
            return PastedColumnRole.Item;
        if (h is "sl" or "sl no" or "s no" or "sno" or "sr" or "sr no" or "no" or "#" or "hsn" or "hsn sac" or "gst" or "gst %" or "unit" or "uom" or "per")
            return PastedColumnRole.Skip;
        return null;
    }

    private static bool IsDescriptionHeading(string cell)
    {
        var h = cell.ToLowerInvariant();
        return (h.Contains("desc") || h.Contains("spec") || h.Contains("detail") || h.Contains("remark"))
               && !h.Contains("name") && !h.Contains("item") && !h.Contains("product");
    }

    /// <summary>The rows read with these columns (the heading row left out).</summary>
    public static List<PastedLine> ParseTable(string? text, IReadOnlyList<PastedColumnRole> roles, bool hasHeader)
    {
        var result = new List<PastedLine>();
        foreach (var row in Cells(text).Skip(hasHeader ? 1 : 0))
        {
            if (result.Count >= MaxLines) break;
            string Join(PastedColumnRole role) =>
                string.Join(' ', row.Where((_, c) => c < roles.Count && roles[c] == role).Where(v => v.Length > 0)).Trim();
            decimal? Number(PastedColumnRole role) =>
                row.Select((v, c) => (v, c)).Where(x => x.c < roles.Count && roles[x.c] == role && TryNumber(x.v, out _, out _, out _))
                    .Select(x => { TryNumber(x.v, out var n, out _, out _); return (decimal?)n; }).FirstOrDefault();

            var part = row.Where((_, c) => c < roles.Count && roles[c] == PastedColumnRole.PartNo).FirstOrDefault(v => v.Length > 0) ?? "";
            var name = Join(PastedColumnRole.Item);
            var query = part.Length > 0 ? part : name;
            if (!query.Any(char.IsLetterOrDigit)) continue;
            var qty = Number(PastedColumnRole.Quantity);
            var rate = Number(PastedColumnRole.Rate);
            var disc = Number(PastedColumnRole.Discount);
            result.Add(new PastedLine(string.Join("  |  ", row.Where(v => v.Length > 0)), query, qty is > 0 ? qty.Value : 1, qty is > 0)
            {
                Rate = rate is >= 0 ? rate : null,
                Discount = disc is >= 0 and < 100 ? disc : null,
                NameQuery = part.Length > 0 && name.Length > 0 ? name : null,
                Description = Join(PastedColumnRole.Description) is { Length: > 0 } d ? d : null,
                Delivery = Join(PastedColumnRole.Delivery) is { Length: > 0 } dl ? dl : null,
            });
        }
        return result;
    }

    /// <summary>A number cell: "15,600.00", "₹ 2450", "Rs. 3800/-", "5%".</summary>
    public static bool TryNumber(string cell, out decimal value, out bool money, out bool percent)
    {
        value = 0;
        var s = cell.Trim();
        percent = s.EndsWith('%');
        if (percent) s = s[..^1].Trim();
        money = false;
        foreach (var sign in new[] { "₹", "rs.", "rs", "inr" })
            if (s.StartsWith(sign, StringComparison.OrdinalIgnoreCase)) { s = s[sign.Length..].Trim(); money = true; break; }
        if (s.EndsWith("/-")) { s = s[..^2].Trim(); money = true; }
        if (s.Contains(',')) money = true;
        s = s.Replace(",", "");
        return s.Length > 0 && s.Any(char.IsDigit) &&
               decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
