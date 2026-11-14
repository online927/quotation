using System.Globalization;
using System.Text.RegularExpressions;
using Quotation.Core.Gst;

namespace Quotation.Core.Formater;

public sealed record FormaterMessage(int Line, string Text)
{
    public override string ToString() => Line > 0 ? $"Line {Line}: {Text}" : Text;
}

/// <summary>One item line: "name, qty, rate[, discount][, unit][, description | description]".</summary>
public sealed class FormaterItem
{
    public int LineNo { get; init; }
    public string Name { get; set; } = "";
    public List<string> Desc { get; } = [];
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "";
    public decimal Rate { get; set; }
    public decimal DiscountPercent { get; set; }
}

public enum FormaterGst { Auto, Igst, CgstSgst, None }

/// <summary>A pasted text with WhatsApp times and names and greeting lines taken out, and how many of each.</summary>
public sealed record FormaterPaste(string Text, int Prefixes, int Lines)
{
    public int Removed => Prefixes + Lines;

    /// <summary>"3 WhatsApp times and names and 2 greeting lines".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Prefixes > 0) parts.Add(Prefixes == 1 ? "1 WhatsApp time and name" : $"{Prefixes} WhatsApp times and names");
        if (Lines > 0) parts.Add(Lines == 1 ? "1 greeting line" : $"{Lines} greeting lines");
        return string.Join(" and ", parts);
    }
}

/// <summary>What was read from the text of the Formater page.</summary>
public sealed class FormaterParse
{
    public string BuyerName { get; set; } = "";
    public List<string> Address { get; } = [];
    public string Gstin { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string OtherRef { get; set; } = "";
    public string OrderNo { get; set; } = "";
    public DateOnly? OrderDate { get; set; }
    public DateOnly? Date { get; set; }
    public string PaymentTerms { get; set; } = "";
    /// <summary>Null: the usual terms &amp; conditions from Settings; empty: none.</summary>
    public List<string>? TermsAndConditions { get; set; }
    public FormaterGst GstMode { get; set; } = FormaterGst.Auto;
    /// <summary>A rate from a "GST: 12%" line (for items not in Tally).</summary>
    public decimal? GstPercent { get; set; }
    public List<FormaterItem> Items { get; } = [];
    public List<FormaterMessage> Errors { get; } = [];
    public List<FormaterMessage> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;

    /// <summary>The state code of the buyer: from the GSTIN, else a state named in the address; "" when unknown.</summary>
    public string StateCode
    {
        get
        {
            if (Gstin.Length >= 2 && Gst.Gstin.IsWellFormed(Gstin)) return Gstin[..2];
            foreach (var line in Address.AsEnumerable().Reverse())
            {
                foreach (var part in line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Reverse())
                {
                    var words = Regex.Replace(part, @"\d", " ").Trim();
                    if (IndianStates.CodeForName(words) is { } code) return code;
                    // "Hyderabad Telangana 500079": try the last one and two words.
                    var w = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (var n = Math.Min(3, w.Length); n >= 1; n--)
                        if (IndianStates.CodeForName(string.Join(' ', w[^n..])) is { } c) return c;
                }
            }
            return "";
        }
    }
}

/// <summary>
/// Reads the text of the Formater page (the way of the separate Formater app): labelled lines (Company:, Address:, GSTIN:,
/// Order No:, …) fill the header; blank lines split the rest into the customer, the other reference and the items. Item lines
/// are "item name, qty, rate, discount" with optional unit and description, with a blank line before each item; the lines
/// under an item are its description lines.
/// </summary>
public static partial class FormaterText
{
    private enum Field { BuyerName, Address, Gstin, Phone, Email, Ref, OtherRef, OrderNo, Date, Terms, Gst, Tc, Ignored, Items }

    private static readonly Dictionary<string, Field> Labels = new(StringComparer.Ordinal)
    {
        ["company"] = Field.BuyerName, ["companyname"] = Field.BuyerName, ["customer"] = Field.BuyerName,
        ["customername"] = Field.BuyerName, ["buyer"] = Field.BuyerName, ["buyername"] = Field.BuyerName,
        ["party"] = Field.BuyerName, ["partyname"] = Field.BuyerName, ["client"] = Field.BuyerName,
        ["clientname"] = Field.BuyerName, ["to"] = Field.BuyerName, ["ms"] = Field.BuyerName,
        ["name"] = Field.BuyerName, ["firm"] = Field.BuyerName,

        ["address"] = Field.Address, ["addr"] = Field.Address, ["add"] = Field.Address,
        ["city"] = Field.Address, ["state"] = Field.Address, ["pin"] = Field.Address,
        ["pincode"] = Field.Address, ["country"] = Field.Address, ["area"] = Field.Address,

        ["gstin"] = Field.Gstin, ["gstno"] = Field.Gstin, ["gstinno"] = Field.Gstin, ["gstn"] = Field.Gstin,
        ["gstnumber"] = Field.Gstin, ["gstinuin"] = Field.Gstin, ["buyergstin"] = Field.Gstin,

        ["phone"] = Field.Phone, ["phoneno"] = Field.Phone, ["ph"] = Field.Phone, ["mobile"] = Field.Phone,
        ["mobileno"] = Field.Phone, ["mob"] = Field.Phone, ["tel"] = Field.Phone, ["telephone"] = Field.Phone,
        ["contact"] = Field.Phone, ["contactno"] = Field.Phone, ["cell"] = Field.Phone, ["whatsapp"] = Field.Phone,

        ["email"] = Field.Email, ["emailid"] = Field.Email, ["mail"] = Field.Email,

        ["ref"] = Field.Ref, ["refno"] = Field.Ref, ["reference"] = Field.Ref, ["referenceno"] = Field.Ref,
        ["refnumber"] = Field.Ref, ["quotationno"] = Field.Ref, ["quotationnumber"] = Field.Ref,
        ["quoteno"] = Field.Ref, ["qno"] = Field.Ref, ["qtnno"] = Field.Ref, ["quotationref"] = Field.Ref,

        ["otherref"] = Field.OtherRef, ["otherrefno"] = Field.OtherRef, ["otherreference"] = Field.OtherRef,
        ["yourref"] = Field.OtherRef, ["yourrefno"] = Field.OtherRef, ["enquiryref"] = Field.OtherRef,
        ["enqref"] = Field.OtherRef, ["enquiryno"] = Field.OtherRef, ["enqno"] = Field.OtherRef,

        ["orderno"] = Field.OrderNo, ["order"] = Field.OrderNo, ["ordernumber"] = Field.OrderNo, ["po"] = Field.OrderNo,
        ["pono"] = Field.OrderNo, ["ponumber"] = Field.OrderNo, ["purchaseorder"] = Field.OrderNo,
        ["purchaseorderno"] = Field.OrderNo, ["buyersref"] = Field.OrderNo, ["buyersrefno"] = Field.OrderNo,
        ["buyersorderno"] = Field.OrderNo, ["buyersreforderno"] = Field.OrderNo,

        ["date"] = Field.Date, ["quotationdate"] = Field.Date,
        ["terms"] = Field.Terms, ["paymentterms"] = Field.Terms, ["payment"] = Field.Terms,
        ["gst"] = Field.Gst, ["tax"] = Field.Gst, ["gsttype"] = Field.Gst, ["taxtype"] = Field.Gst,
        ["gstrate"] = Field.Gst,

        ["t&c"] = Field.Tc, ["tc"] = Field.Tc, ["terms&conditions"] = Field.Tc,
        ["termsandconditions"] = Field.Tc, ["termsconditions"] = Field.Tc, ["conditions"] = Field.Tc,
        ["condition"] = Field.Tc, ["note"] = Field.Tc, ["notes"] = Field.Tc, ["remark"] = Field.Tc,
        ["remarks"] = Field.Tc,

        // Lines of the Formater app that this page does not use (the sign-off is no longer printed).
        ["signoff"] = Field.Ignored, ["signature"] = Field.Ignored, ["closing"] = Field.Ignored,
        ["title"] = Field.Ignored, ["document"] = Field.Ignored,
        ["items"] = Field.Items, ["item"] = Field.Items, ["products"] = Field.Items, ["particulars"] = Field.Items,
    };

    public static readonly string[] Units =
    [
        "Nos", "No", "Pcs", "Pc", "Piece", "Pieces", "Set", "Sets", "Pair", "Pairs", "Kg", "Kgs", "G", "Gm", "Gms",
        "Mtr", "Mtrs", "M", "Meter", "Meters", "Metre", "Metres", "Ltr", "Ltrs", "L", "Box", "Boxes", "Pkt", "Pkts",
        "Pack", "Packs", "Roll", "Rolls", "Each", "Ea", "Unit", "Units", "Dozen", "Dz", "Lot", "Lots", "Job", "Jobs",
        "Hr", "Hrs", "Sqft", "Ft", "Feet", "Mm", "Cm", "Bag", "Bags", "Bottle", "Bottles", "Can", "Cans", "Tube",
        "Tubes", "Reel", "Reels", "Coil", "Coils", "Sheet", "Sheets", "Length", "Lengths", "Kit", "Kits", "Bundle",
        "Bundles", "Carton", "Cartons", "Drum", "Drums",
    ];

    public const string DefaultUnit = "Nos";

    [GeneratedRegex(@"^(?<k>[A-Za-z][A-Za-z0-9 .&/_'-]{0,30}?)\s*[:=]\s*(?<v>.*)$")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"^(?:gstin|gst\s*no\.?|gstn)\s*[#.-]?\s*(?<v>[0-9A-Za-z]{15})$", RegexOptions.IgnoreCase)]
    private static partial Regex GstinNoColonRegex();

    [GeneratedRegex(@"^[^\s@,]+@[^\s@,]+\.[^\s@,]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\+?\d[\d\s()-]{7,}\d$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^(?:disc(?:ount)?\.?\s*)?(?<v>\d+(?:\.\d+)?)\s*%?(?:\s*(?:disc(?:ount)?|off))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscountRegex();

    [GeneratedRegex(@"^(?<v>\d+(?:\.\d+)?)\s*(?<u>[A-Za-z][A-Za-z.]{0,9})?$")]
    private static partial Regex QtyRegex();

    [GeneratedRegex(@"^(?:rs\.?|inr|₹)?\s*(?<v>(?:0|[1-9]\d*)(?:\.\d+)?)\s*(?:/-)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RateRegex();

    [GeneratedRegex(@"^\d{2,3}(?:\.\d+)?(?:\s*/-)?$")]
    private static partial Regex DigitGroupRegex();

    [GeneratedRegex(@"^\d{1,3}\s*[.)]\s+")]
    private static partial Regex LeadingNumberRegex();

    // "PO/4471 dt 28/10/2026", "PO/4471 dated 28-Oct-2026", "PO/4471, 28.10.2026"
    [GeneratedRegex(@"^(?<no>.+?)\s*(?:,|\b(?:dt|dtd|dated|date)\b\.?:?)\s*(?<d>\d{1,2}[./-]\w{1,9}[./-]\d{2,4}|\d{1,2}\s+\w{3,9}\s+\d{4})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OrderWithDateRegex();

    private static readonly string[] DateFormats =
    [
        "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy", "d MMMM yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy",
        "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd-MMM-yyyy", "d-MMM-yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yy",
    ];

    public static DateOnly? ParseDate(string value) =>
        DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? DateOnly.FromDateTime(d)
            : null;

    /// <summary>What the unlabelled lines of a block (lines between blank lines) are.</summary>
    private enum Role { None, Party, OtherRef, Items }

    [GeneratedRegex(@"^(?:thanks?|thank\s+you|thx|regards|best\s+regards|warm\s+regards)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SignOffRegex();

    /// <summary>
    /// Blank lines split the text into parts: first the customer (its first line is the name; the next lines are the
    /// address, GSTIN, phone, e-mail), then the other reference, then the items. A part whose lines read as items is items
    /// at once. Labelled lines (Company:, Order No:, GST: …) may stand anywhere.
    /// </summary>
    public static FormaterParse Parse(string? text)
    {
        var r = new FormaterParse();
        string? gstin = null, phone = null, email = null;
        List<string>? tc = null;
        var units = new HashSet<string>(Units, StringComparer.OrdinalIgnoreCase);
        Field? section = null;
        var role = Role.None;
        bool partyDone = false, otherRefDone = false, itemsStarted = false;
        FormaterItem? blockItem = null; // the item that started the current part of the items (its next lines are its description)

        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0)
            {
                // A blank line ends the part.
                if (role == Role.Party) partyDone = true;
                if (role == Role.OtherRef) otherRefDone = true;
                role = itemsStarted ? Role.Items : Role.None;
                section = null;
                blockItem = null;
                continue;
            }
            if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;

            var indented = char.IsWhiteSpace(raw[0]);
            var bullet = line[0] is '-' or '•' or '*' or '>' or '–';

            var unbulleted = bullet ? line.TrimStart('-', '•', '*', '>', '–', ' ') : line;

            // Indented or "-" lines under an item are its description, even when they hold a ":" (unless the line is
            // clearly an item itself, as in a pasted list).
            if (role == Role.Items && blockItem is not null && (indented || bullet) &&
                TryItem(unbulleted, lineNo, units, strict: true, out var notItem) is null && notItem is null)
            {
                blockItem.Desc.Add(unbulleted);
                continue;
            }

            if (TryLabel(line, out var field, out var value))
            {
                if (value.Length == 0)
                {
                    section = field;
                    if (field == Field.Tc) tc ??= [];
                    if (field == Field.Items)
                    {
                        section = null;
                        itemsStarted = true;
                        role = Role.Items;
                        blockItem = null;
                    }
                    continue;
                }
                section = null;
                switch (field)
                {
                    case Field.BuyerName:
                        if (r.BuyerName.Length > 0) r.Warnings.Add(new(lineNo, $"Company given twice; using \"{value}\"."));
                        r.BuyerName = value;
                        break;
                    case Field.Address: r.Address.Add(value); break;
                    case Field.Gstin:
                        gstin = value.ToUpperInvariant().Replace(" ", "");
                        if (!Gst.Gstin.IsWellFormed(gstin)) r.Warnings.Add(new(lineNo, $"\"{value}\" does not look like a 15-character GSTIN."));
                        break;
                    case Field.Phone: phone = value; break;
                    case Field.Email: email = value; break;
                    case Field.Ref: r.Warnings.Add(new(lineNo, "The quotation number is given by the app; this Ref line is left out.")); break;
                    case Field.OtherRef: r.OtherRef = value; break;
                    case Field.OrderNo: ReadOrder(value, r); break;
                    case Field.Date:
                        r.Date = ParseDate(value);
                        if (r.Date is null) r.Warnings.Add(new(lineNo, $"Date \"{value}\" not understood; today's date is used."));
                        break;
                    case Field.Terms: r.PaymentTerms = value; break;
                    case Field.Gst: ReadGst(value, lineNo, r); break;
                    case Field.Tc: (tc ??= []).Add(value); break;
                    case Field.Ignored: break;
                }
                continue;
            }
            if (section == Field.Tc) { tc!.Add(line); continue; }
            if (section == Field.Ignored) continue;
            if (section == Field.Address) { AddPartyLine(unbulleted, r.Address, ref gstin, ref phone, ref email); continue; }

            // In the customer part a "-" line is an address line (the "-" is not printed), never an item.
            if (bullet && role == Role.Party && unbulleted.Length > 0)
            {
                AddPartyLine(unbulleted, r.Address, ref gstin, ref phone, ref email);
                continue;
            }

            // A line that is clearly an item ("name, qty, rate") starts the items, wherever it stands.
            // (A line that reads as an item but has a mistake, such as "Gauge, 10%, five, 12011", is an item too: it is shown as a problem.)
            var clearItem = role != Role.Items && (TryItem(line, lineNo, units, strict: true, out var looksLikeItem) is not null || looksLikeItem is not null);
            if (role == Role.None)
            {
                role = itemsStarted || clearItem ? Role.Items
                    : !partyDone ? Role.Party
                    // "Dial Gauge, 2" where the other reference would be: the items (without the other reference).
                    : !otherRefDone && r.OtherRef.Length == 0 && !(line.Contains(',') && TryItem(line, lineNo, units, strict: false, out _) is not null) ? Role.OtherRef
                    : Role.Items;
            }
            else if (clearItem)
            {
                role = Role.Items;
            }
            if (role == Role.Items) itemsStarted = true;

            switch (role)
            {
                case Role.Party:
                    if (r.BuyerName.Length == 0)
                    {
                        if (line.Equals("to", StringComparison.OrdinalIgnoreCase) || line.Equals("to,", StringComparison.OrdinalIgnoreCase)) continue;
                        r.BuyerName = unbulleted;
                    }
                    else
                    {
                        AddPartyLine(unbulleted, r.Address, ref gstin, ref phone, ref email);
                    }
                    continue;
                case Role.OtherRef:
                    if (LooksLikeContact(line)) AddPartyLine(line, r.Address, ref gstin, ref phone, ref email);
                    else r.OtherRef = r.OtherRef.Length == 0 ? line : r.OtherRef + " " + line;
                    continue;
            }

            // The items part: a blank line before each item; the first line of each part is the item ("name, qty, rate,
            // discount") and the lines under it are its description, unless a line is clearly an item itself (a pasted list).
            if (!line.Any(char.IsDigit) && SignOffRegex().IsMatch(line)) continue;
            string? itemError;
            FormaterItem? item;
            if (blockItem is not null)
            {
                item = TryItem(unbulleted, lineNo, units, strict: true, out itemError);
                if (item is null && itemError is null)
                {
                    blockItem.Desc.Add(unbulleted);
                    continue;
                }
            }
            else
            {
                item = TryItem(unbulleted, lineNo, units, strict: false, out itemError);
            }
            if (item is null)
            {
                r.Errors.Add(new(lineNo, itemError ?? $"Could not read this item. Use:  item name, qty, rate, discount   (got \"{line}\")"));
                continue;
            }
            if (item.DiscountPercent >= 100) r.Errors.Add(new(lineNo, $"Discount {item.DiscountPercent:0.##}% must be below 100%."));
            if (item.Qty == 0) r.Errors.Add(new(lineNo, $"Qty is 0 for \"{item.Name}\"."));
            if (item.Rate == 0) r.Warnings.Add(new(lineNo, $"Rate not given for \"{item.Name}\": the Tally rate is used when the item is in Tally."));
            r.Items.Add(item);
            blockItem = item;
        }

        r.Gstin = gstin ?? "";
        r.Phone = phone ?? "";
        r.Email = email ?? "";
        r.TermsAndConditions = tc;
        if (r.BuyerName.Length == 0) r.Errors.Add(new(0, "The customer is missing: type the customer's name on the first line."));
        if (r.Items.Count == 0) r.Errors.Add(new(0, "No items found. After a blank line, type each item as:  item name, qty, rate, discount"));
        return r;
    }

    private static void ReadOrder(string value, FormaterParse r)
    {
        var m = OrderWithDateRegex().Match(value);
        if (m.Success && ParseDate(m.Groups["d"].Value) is { } d)
        {
            r.OrderNo = m.Groups["no"].Value.Trim().TrimEnd(',', '-');
            r.OrderDate = d;
        }
        else
        {
            r.OrderNo = value.Trim();
        }
    }

    private static void ReadGst(string value, int lineNo, FormaterParse r)
    {
        var v = value.ToLowerInvariant();
        var number = Regex.Match(v, @"\d+(?:\.\d+)?");
        if (number.Success) r.GstPercent = decimal.Parse(number.Value, CultureInfo.InvariantCulture);
        if (Regex.IsMatch(v, @"\b(none|nil|no|without|exempt|exempted)\b") || r.GstPercent == 0) r.GstMode = FormaterGst.None;
        else if (v.Contains("cgst") || v.Contains("sgst") || v.Contains("intra") || v.Contains("local")) r.GstMode = FormaterGst.CgstSgst;
        else if (v.Contains("igst") || v.Contains("inter")) r.GstMode = FormaterGst.Igst;
        else if (v.Contains("auto") || number.Success) r.GstMode = FormaterGst.Auto;
        else r.Warnings.Add(new(lineNo, $"GST \"{value}\" not understood; use IGST, CGST+SGST, None or a rate like 18%."));
    }

    private static bool TryLabel(string line, out Field field, out string value)
    {
        field = default;
        value = "";
        var m = LabelRegex().Match(line);
        if (m.Success && Labels.TryGetValue(NormalizeKey(m.Groups["k"].Value), out field))
        {
            value = m.Groups["v"].Value.Trim();
            return true;
        }
        m = GstinNoColonRegex().Match(line);
        if (m.Success)
        {
            field = Field.Gstin;
            value = m.Groups["v"].Value.Trim();
            return true;
        }
        return false;
    }

    private static string NormalizeKey(string key)
    {
        var k = new string(key.ToLowerInvariant().Where(c => char.IsLetter(c) || c == '&').ToArray());
        return k.StartsWith("address", StringComparison.Ordinal) || k.StartsWith("addr", StringComparison.Ordinal) ? "address" : k;
    }

    private static bool LooksLikeContact(string line)
    {
        var compact = line.Replace(" ", "");
        return (compact.Length == 15 && Gst.Gstin.IsWellFormed(compact)) || EmailRegex().IsMatch(line) || PhoneRegex().IsMatch(line);
    }

    private static void AddPartyLine(string line, List<string> address, ref string? gstin, ref string? phone, ref string? email)
    {
        var compact = line.Replace(" ", "");
        if (gstin is null && compact.Length == 15 && Gst.Gstin.IsWellFormed(compact)) gstin = compact.ToUpperInvariant();
        else if (email is null && EmailRegex().IsMatch(line)) email = line;
        else if (phone is null && PhoneRegex().IsMatch(line)) phone = line;
        else address.Add(line);
    }

    /// <summary>
    /// Reads an item line: "name, qty, rate[, discount]" (the discount is a plain number; "%" may be typed). The older order
    /// "name, discount%, qty, rate" is still read. Unit and description may follow ("…, Pcs, Make - Insize | 500-196").
    /// Outside strict mode also "name, qty" (the Tally rate) and "name qty rate [disc%]" with spaces.
    /// </summary>
    private static FormaterItem? TryItem(string line, int lineNo, HashSet<string> units, bool strict, out string? error)
    {
        error = null;
        var tabs = line.Contains('\t');
        if (!tabs && !line.Contains(','))
            return strict ? null : TrySpacedItem(line, lineNo);
        var fields = (tabs ? line.Split('\t') : line.Split(',')).Select(f => f.Trim()).ToList();
        if (tabs)
        {
            while (fields.Count > 0 && fields[0].Length == 0) fields.RemoveAt(0);
            while (fields.Count > 0 && fields[^1].Length == 0) fields.RemoveAt(fields.Count - 1);
            if (fields.Count >= 4 && Regex.IsMatch(fields[0], @"^\d{1,3}\.?$")) fields.RemoveAt(0);
            for (var k = 1; k < fields.Count; k++)
                if (Regex.IsMatch(fields[k], @"^[₹\d,.\s%/-]+$|^(rs\.?|inr)", RegexOptions.IgnoreCase)) fields[k] = fields[k].Replace(",", "");
        }

        FormaterItem? fallback = null;
        for (var i = 1; i < fields.Count; i++)
        {
            // A "%" in the field after the name: the older order (name, discount%, qty, rate).
            var oldOrder = fields[i].Contains('%');
            var item = oldOrder ? OldOrderItem(fields, i, tabs, lineNo, units) : NewOrderItem(fields, i, tabs, lineNo, units, strict);
            if (item is not null && Fine(item)) return item;
            if (!oldOrder && OldOrderItem(fields, i, tabs, lineNo, units) is { } old && Fine(old)) return old;
            fallback ??= item;
        }
        if (fallback is not null) return fallback;

        var numeric = fields.Skip(1).Count(f => f.Length > 0 && Regex.IsMatch(f, @"\d") &&
                                                Regex.IsMatch(f, @"^[₹\d.,%\s/-]+$|^(rs\.?|inr)", RegexOptions.IgnoreCase));
        // In the items part a line with a plain number in it is a broken item, not a description ("Range 0-150, 0.01mm" is one).
        if ((fields.Count >= 3 && numeric >= 2) || (!strict && fields.Skip(1).Any(f => Regex.IsMatch(f, @"^\d+(?:\.\d+)?$")))) error = $"Could not read this item. Use:  item name, qty, rate, discount   (got \"{line}\")";
        return null;

        static bool Fine(FormaterItem it) => it.Qty > 0 && it.DiscountPercent < 100;
    }

    private static string ItemName(List<string> fields, int count) =>
        LeadingNumberRegex().Replace(string.Join(", ", fields.Take(count)).Trim(), "");

    /// <summary>"name, qty, rate[, discount][, unit][, description]" with the qty at <paramref name="i"/>.</summary>
    private static FormaterItem? NewOrderItem(List<string> fields, int i, bool tabs, int lineNo, HashSet<string> units, bool strict)
    {
        var qm = QtyRegex().Match(fields[i]);
        var name = ItemName(fields, i);
        if (!qm.Success || name.Length == 0) return null;
        var unit = qm.Groups["u"].Success && units.Contains(qm.Groups["u"].Value) ? qm.Groups["u"].Value : "";
        if (qm.Groups["u"].Success && unit.Length == 0) return null;

        if (i + 1 >= fields.Count)
        {
            // "name, qty": the rate comes from Tally. Only a whole number of pieces ("Range 0-150mm, 0.01mm" is a description).
            if (strict || fields.Count != 2 || qm.Groups["v"].Value.Contains('.') || unit.ToLowerInvariant() is "mm" or "cm" or "m" or "ft") return null;
            return new FormaterItem
            {
                LineNo = lineNo, Name = name, Qty = decimal.Parse(qm.Groups["v"].Value, CultureInfo.InvariantCulture),
                Unit = unit.Length > 0 ? unit : DefaultUnit,
            };
        }
        var rm = RateRegex().Match(fields[i + 1]);
        if (!rm.Success) return null;
        var (rateText, next) = JoinThousands(fields, rm.Groups["v"].Value, i + 2, tabs);

        var item = new FormaterItem
        {
            LineNo = lineNo, Name = name, Unit = unit,
            Qty = decimal.Parse(qm.Groups["v"].Value, CultureInfo.InvariantCulture),
            Rate = decimal.Parse(rateText, CultureInfo.InvariantCulture),
        };
        var extras = fields.Skip(next).ToList();
        // The discount: the field right after the rate, a plain number ("5" or "5%"); empty or "-" is no discount.
        if (extras.Count > 0 && ParseDiscount(extras[0]) is { } disc)
        {
            item.DiscountPercent = disc;
            extras.RemoveAt(0);
        }
        AddUnitAndDescription(item, extras.Where(f => f.Length > 0).ToList(), units);
        return item;
    }

    /// <summary>The older order "name, discount, qty, rate[, unit][, description]" with the discount at <paramref name="i"/>.</summary>
    private static FormaterItem? OldOrderItem(List<string> fields, int i, bool tabs, int lineNo, HashSet<string> units)
    {
        if (i + 2 >= fields.Count) return null;
        var disc = ParseDiscount(fields[i]);
        var qm = QtyRegex().Match(fields[i + 1]);
        var rm = RateRegex().Match(fields[i + 2]);
        var name = ItemName(fields, i);
        if (disc is null || !qm.Success || !rm.Success || name.Length == 0) return null;
        var (rateText, next) = JoinThousands(fields, rm.Groups["v"].Value, i + 3, tabs);
        var item = new FormaterItem
        {
            LineNo = lineNo, Name = name, DiscountPercent = disc.Value,
            Qty = decimal.Parse(qm.Groups["v"].Value, CultureInfo.InvariantCulture),
            Rate = decimal.Parse(rateText, CultureInfo.InvariantCulture),
            Unit = qm.Groups["u"].Success && units.Contains(qm.Groups["u"].Value) ? qm.Groups["u"].Value : "",
        };
        AddUnitAndDescription(item, fields.Skip(next).Where(f => f.Length > 0).ToList(), units);
        return item;
    }

    /// <summary>A rate typed with thousand separators: "12,011" or "1,23,456.50" (the commas also split the fields).</summary>
    private static (string Rate, int Next) JoinThousands(List<string> fields, string rateText, int next, bool tabs)
    {
        if (tabs || !Regex.IsMatch(rateText, @"^\d{1,3}$")) return (rateText, next);
        var run = 0;
        while (next + run < fields.Count && DigitGroupRegex().IsMatch(fields[next + run])) run++;
        for (var len = run; len > 0; len--)
        {
            var groups = fields.Skip(next).Take(len).Select(g => g.Replace("/-", "").Trim()).ToList();
            if (groups[^1].Split('.')[0].Length != 3 || groups.Take(len - 1).Any(g => g.Contains('.'))) continue;
            return (rateText + string.Concat(groups), next + len);
        }
        return (rateText, next);
    }

    private static void AddUnitAndDescription(FormaterItem item, List<string> extras, HashSet<string> units)
    {
        if (extras.Count > 0 && units.Contains(extras[0]))
        {
            if (item.Unit.Length == 0) item.Unit = extras[0];
            extras.RemoveAt(0);
        }
        if (item.Unit.Length == 0) item.Unit = DefaultUnit;
        if (extras.Count > 0)
            item.Desc.AddRange(string.Join(", ", extras).Split('|').Select(d => d.Trim()).Where(d => d.Length > 0));
    }

    [GeneratedRegex(@"^(?<name>.*?\S)\s+(?<q>\d+(?:\.\d+)?)\s+(?:rs\.?\s*|₹\s*)?(?<r>\d+(?:\.\d+)?)(?:\s*/-)?(?:\s+(?<d>\d+(?:\.\d+)?)\s*%)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SpacedItemRegex();

    /// <summary>"530-104 5 3250" or "530-104 5 3250 5%": the last two numbers are the qty and the rate.</summary>
    private static FormaterItem? TrySpacedItem(string line, int lineNo)
    {
        var m = SpacedItemRegex().Match(line);
        if (!m.Success) return null;
        var qty = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
        var name = LeadingNumberRegex().Replace(m.Groups["name"].Value.Trim(), "");
        if (qty <= 0 || name.Length == 0) return null;
        return new FormaterItem
        {
            LineNo = lineNo, Name = name, Qty = qty, Unit = DefaultUnit,
            Rate = decimal.Parse(m.Groups["r"].Value, CultureInfo.InvariantCulture),
            DiscountPercent = m.Groups["d"].Success ? decimal.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture) : 0,
        };
    }

    private static decimal? ParseDiscount(string field)
    {
        var f = field.Trim();
        if (f.Length == 0 || f is "-" or "–" || f.Equals("nil", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("no", StringComparison.OrdinalIgnoreCase) || f.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0m;
        var m = DiscountRegex().Match(f);
        return m.Success ? decimal.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture) : null;
    }

    // ---------------------------------------------------------------- pasted text

    // "[11/11, 10:15 am] Ravi: …" (WhatsApp copy) and "11/11/26, 10:15 am - Ravi: …" (WhatsApp export)
    [GeneratedRegex(@"^\s*(?:\[[^\]]{3,40}\]\s*[^:\n]{1,40}:\s*|\d{1,2}/\d{1,2}/\d{2,4},?\s+\d{1,2}:\d{2}(?:\s*[ap]\.?\s?m\.?)?\s+-\s+[^:\n]{1,40}:\s*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex WhatsAppPrefixRegex();

    // Lines that are only a greeting or a closing (never when they hold a number).
    [GeneratedRegex(@"^(?:hi|hello|hey|hii+|dear\b.*|good\s+(?:morning|afternoon|evening|day)|greetings|thanks?\b.*|thank\s+you\b.*|thx|tq|ty|" +
                    @"(?:best|warm|kind|with)?\s*regards\b.*|yours\s+(?:truly|faithfully|sincerely)|sir|madam|ma'?am|noted|ok|okay|" +
                    @"(?:please|pls|plz|kindly)\s+(?:quote|send|share|find|give|provide)\b.*|need\s+(?:a\s+)?(?:quotation|quote|rates?|price)\b.*|" +
                    @"assalamu?\s*-?\s*alaikum\b.*|salaam?|<media omitted>|this message was deleted|image omitted)" +
                    @"(?:[\s,]+(?:sir|madam|ma'?am|all|team|bro|bhai|ji|sahab|saab))*[\s,.!:;-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingLineRegex();

    /// <summary>
    /// A pasted text made ready for the Formater page: WhatsApp times and names at the start of lines and lines that are only a
    /// greeting ("Hi sir", "Good morning", "Thanks", "Regards") are taken out; more than one blank line in a row becomes one.
    /// </summary>
    public static FormaterPaste CleanPasted(string? text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>();
        int prefixes = 0, removed = 0;
        foreach (var raw in lines)
        {
            var line = raw;
            var m = WhatsAppPrefixRegex().Match(line);
            if (m.Success)
            {
                line = line[m.Length..];
                prefixes++;
            }
            var t = line.Trim();
            if (t.Length > 0 && !t.Any(char.IsDigit) && GreetingLineRegex().IsMatch(t))
            {
                removed++;
                continue;
            }
            if (t.Length == 0 && kept.Count > 0 && kept[^1].Trim().Length == 0) continue; // one blank line is enough
            kept.Add(line.TrimEnd());
        }
        while (kept.Count > 0 && kept[0].Trim().Length == 0 && removed + prefixes > 0) kept.RemoveAt(0);
        return new FormaterPaste(string.Join("\n", kept), prefixes, removed);
    }
}
