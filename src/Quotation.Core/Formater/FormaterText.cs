using System.Globalization;
using System.Text.RegularExpressions;
using Quotation.Core.Gst;

namespace Quotation.Core.Formater;

public sealed record FormaterMessage(int Line, string Text)
{
    public override string ToString() => Line > 0 ? $"Line {Line}: {Text}" : Text;
}

/// <summary>One item line: "name, discount, qty, rate[, unit][, description | description]".</summary>
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
/// Order No:, …) fill the header; item lines are "item name, discount, qty, rate" with optional unit and description;
/// indented or "-" lines under an item are its description lines.
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

    public static FormaterParse Parse(string? text)
    {
        var r = new FormaterParse();
        string? gstin = null, phone = null, email = null;
        List<string>? tc = null;
        var units = new HashSet<string>(Units, StringComparer.OrdinalIgnoreCase);
        FormaterItem? lastItem = null;
        Field? section = null;
        var otherRefDone = false;

        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (section is Field.Tc or Field.Address or Field.Ignored) section = null;
                continue;
            }
            if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;

            var indented = char.IsWhiteSpace(raw[0]);
            var bullet = line[0] is '-' or '•' or '*' or '>' or '–';

            if (TryLabel(line, out var field, out var value))
            {
                if (value.Length == 0)
                {
                    section = field;
                    if (field == Field.Tc) tc ??= [];
                    if (field == Field.Items) lastItem = null;
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

            var item = TryItem(line, lineNo, units, out var itemError);
            if (item is not null)
            {
                if (item.DiscountPercent >= 100) r.Errors.Add(new(lineNo, $"Discount {item.DiscountPercent:0.##}% must be below 100%."));
                if (item.Qty == 0) r.Errors.Add(new(lineNo, $"Qty is 0 for \"{item.Name}\"."));
                if (item.Rate == 0) r.Warnings.Add(new(lineNo, $"Rate is 0 for \"{item.Name}\": the Tally rate is used when the item is in Tally."));
                r.Items.Add(item);
                lastItem = item;
                section = null;
                continue;
            }
            if (itemError is not null && !(lastItem is not null && (indented || bullet)))
            {
                r.Errors.Add(new(lineNo, itemError));
                continue;
            }
            if (section == Field.Tc) { tc!.Add(line); continue; }
            if (section == Field.Ignored) continue;
            if (section == Field.Address) { AddPartyLine(line, r.Address, ref gstin, ref phone, ref email); continue; }
            if (lastItem is not null)
            {
                lastItem.Desc.Add(bullet ? line.TrimStart('-', '•', '*', '>', '–', ' ') : line);
                continue;
            }
            if (r.BuyerName.Length == 0)
            {
                if (line.Equals("to", StringComparison.OrdinalIgnoreCase) || line.Equals("to,", StringComparison.OrdinalIgnoreCase)) continue;
                r.BuyerName = line;
                continue;
            }
            // The line after the customer is the other reference (unless it is a GSTIN, phone or e-mail, or one was labelled).
            if (!otherRefDone && r.OtherRef.Length == 0 && !LooksLikeContact(line))
            {
                r.OtherRef = line;
                otherRefDone = true;
                continue;
            }
            otherRefDone = true;
            AddPartyLine(line, r.Address, ref gstin, ref phone, ref email);
        }

        r.Gstin = gstin ?? "";
        r.Phone = phone ?? "";
        r.Email = email ?? "";
        r.TermsAndConditions = tc;
        if (r.BuyerName.Length == 0) r.Errors.Add(new(0, "The customer is missing: type the customer's name on the first line."));
        if (r.Items.Count == 0) r.Errors.Add(new(0, "No items found. Item lines look like:  Item name, discount, qty, rate"));
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

    private static FormaterItem? TryItem(string line, int lineNo, HashSet<string> units, out string? error)
    {
        error = null;
        var tabs = line.Contains('\t');
        var fields = (tabs ? line.Split('\t') : line.Split(',')).Select(f => f.Trim()).ToList();
        if (tabs)
        {
            while (fields.Count > 0 && fields[0].Length == 0) fields.RemoveAt(0);
            while (fields.Count > 0 && fields[^1].Length == 0) fields.RemoveAt(fields.Count - 1);
            if (fields.Count >= 5 && Regex.IsMatch(fields[0], @"^\d{1,3}\.?$")) fields.RemoveAt(0);
            for (var k = 1; k < fields.Count; k++)
                if (Regex.IsMatch(fields[k], @"^[₹\d,.\s%/-]+$|^(rs\.?|inr)", RegexOptions.IgnoreCase)) fields[k] = fields[k].Replace(",", "");
        }

        for (var i = 1; i + 2 < fields.Count; i++)
        {
            var disc = ParseDiscount(fields[i]);
            var qm = QtyRegex().Match(fields[i + 1]);
            var rm = RateRegex().Match(fields[i + 2]);
            if (disc is null || !qm.Success || !rm.Success) continue;
            var name = LeadingNumberRegex().Replace(string.Join(", ", fields.Take(i)).Trim(), "");
            if (name.Length == 0) continue;

            // A rate typed with thousand separators: "12,011" or "1,23,456.50".
            var rateText = rm.Groups["v"].Value;
            var next = i + 3;
            if (!tabs && Regex.IsMatch(rateText, @"^\d{1,3}$"))
            {
                var run = 0;
                while (next + run < fields.Count && DigitGroupRegex().IsMatch(fields[next + run])) run++;
                for (var len = run; len > 0; len--)
                {
                    var groups = fields.Skip(next).Take(len).Select(g => g.Replace("/-", "").Trim()).ToList();
                    if (groups[^1].Split('.')[0].Length != 3 || groups.Take(len - 1).Any(g => g.Contains('.'))) continue;
                    rateText += string.Concat(groups);
                    next += len;
                    break;
                }
            }

            var item = new FormaterItem
            {
                LineNo = lineNo,
                Name = name,
                DiscountPercent = disc.Value,
                Qty = decimal.Parse(qm.Groups["v"].Value, CultureInfo.InvariantCulture),
                Rate = decimal.Parse(rateText, CultureInfo.InvariantCulture),
                Unit = qm.Groups["u"].Success && units.Contains(qm.Groups["u"].Value) ? qm.Groups["u"].Value : "",
            };
            var extras = fields.Skip(next).Where(f => f.Length > 0).ToList();
            if (extras.Count > 0 && units.Contains(extras[0]))
            {
                if (item.Unit.Length == 0) item.Unit = extras[0];
                extras.RemoveAt(0);
            }
            if (item.Unit.Length == 0) item.Unit = DefaultUnit;
            if (extras.Count > 0)
                item.Desc.AddRange(string.Join(", ", extras).Split('|').Select(d => d.Trim()).Where(d => d.Length > 0));
            return item;
        }

        var numeric = fields.Skip(1).Count(f => f.Length > 0 && Regex.IsMatch(f, @"\d") &&
                                                Regex.IsMatch(f, @"^[₹\d.,%\s/-]+$|^(rs\.?|inr)", RegexOptions.IgnoreCase));
        if (fields.Count >= 3 && numeric >= 2) error = $"Could not read this item. Use:  item name, discount, qty, rate   (got \"{line}\")";
        return null;
    }

    private static decimal? ParseDiscount(string field)
    {
        var f = field.Trim();
        if (f.Length == 0 || f is "-" or "–" || f.Equals("nil", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("no", StringComparison.OrdinalIgnoreCase) || f.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0m;
        var m = DiscountRegex().Match(f);
        return m.Success ? decimal.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture) : null;
    }
}
