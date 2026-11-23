using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;

namespace Quotation.Pdf;

/// <summary>
/// The "Minimal" layout (Round 35, design B of docs/handoff/previews/137-new-pdf-layouts.png): lots of white space, thin
/// lines, small grey capital labels, no colour. Company top left, the title and number top right; Buyer / Ship to /
/// Reference in three columns; the items; totals on the right; terms, bank and the signature at the foot of the last page.
/// Further pages repeat the table heading. Positions are in points from the top-left corner.
/// </summary>
public sealed class MinimalPdfRenderer
{
    private const double PageW = QuotationPdfRenderer.A4Width, PageH = QuotationPdfRenderer.A4Height;
    private const double Left = 48, RightEdge = PageW - 48, Width = RightEdge - Left;
    private static readonly XColor Ink = XColor.FromArgb(0x22, 0x22, 0x22);
    private static readonly XColor Grey = XColor.FromArgb(0x77, 0x77, 0x77);
    private static readonly XColor Label = XColor.FromArgb(0x99, 0x99, 0x99);
    private static readonly XColor Hair = XColor.FromArgb(0xE6, 0xE6, 0xE6);

    // Item table columns (right edges for numbers).
    private const double NoX = Left, ItemX = Left + 24, HsnX = 300, GstRight = 360, QtyRight = 412, RateRight = 468, DiscRight = 500, AmountRight = RightEdge;
    private const double ItemWidth = HsnX - ItemX - 10;
    private const double Step = 10.5;
    private const double FooterTop = 800;

    private readonly Dictionary<(double, bool), XFont> _fonts = [];
    private readonly Lock _lock = new();

    public MinimalPdfRenderer() => PdfFonts.EnsureInstalled();

    private XFont F(double size, bool bold = false)
    {
        if (!_fonts.TryGetValue((size, bold), out var f)) _fonts[(size, bold)] = f = new XFont(PdfFonts.Ubuntu, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        return f;
    }

    public byte[] Render(QuotationDocument doc)
    {
        lock (_lock)
        {
            using var pdf = new PdfDocument();
            pdf.Info.Title = $"{doc.Title} {doc.Number}";
            pdf.Info.Author = doc.Company.CompanyName;
            pdf.Info.Creator = "Quotations";

            var measure = XGraphics.FromPdfPage(new PdfDocument().AddPage());
            var rows = doc.Lines.Select(l => MakeRow(measure, doc, l)).ToList();
            var headerBottom = HeaderBottom(measure, doc);
            var bottom = BottomBlock(measure, doc);
            var totals = Totals(doc);
            var totalsHeight = 16 * totals.Count + 26 + (string.IsNullOrWhiteSpace(doc.AmountInWords) ? 0 : 14)
                               + (string.IsNullOrWhiteSpace(doc.Remarks) ? 0 : 8 + Step * Wrap(measure, "Remarks: " + doc.Remarks, F(8.5, true), Width).Count);
            var bottomTop = FooterTop - 14 - bottom.Height;

            // Rows per page: the last page also needs the totals and the bottom block.
            var pages = new List<List<Row>> { new() };
            double TableTop(int page) => page == 0 ? headerBottom + 26 : 60;
            var y = TableTop(0) + 22;
            foreach (var row in rows)
            {
                if (y + row.Height > FooterTop - 20 && pages[^1].Count > 0)
                {
                    pages.Add([]);
                    y = TableTop(pages.Count - 1) + 22;
                }
                pages[^1].Add(row);
                y += row.Height;
            }
            while (TableTop(pages.Count - 1) + 22 + pages[^1].Sum(r => r.Height) + totalsHeight > bottomTop && pages[^1].Count > 0)
            {
                var last = pages[^1];
                if (last.Count == 1) { pages.Add([]); break; }
                var carry = last[^1];
                last.RemoveAt(last.Count - 1);
                pages.Add([carry]);
            }

            for (var p = 0; p < pages.Count; p++)
            {
                var page = pdf.AddPage();
                page.Width = XUnit.FromPoint(PageW);
                page.Height = XUnit.FromPoint(PageH);
                using var g = XGraphics.FromPdfPage(page);
                if (p == 0) DrawHeader(g, doc);
                else Text(g, Left, 34, $"{doc.Title} {doc.Number} (continued)", 8, color: Grey);
                var tableTop = TableTop(p);
                var after = DrawTable(g, doc, pages[p], tableTop);
                var last = p == pages.Count - 1;
                if (last)
                {
                    DrawTotals(g, doc, totals, after + 10);
                    DrawBottom(g, doc, bottom, bottomTop);
                    if (doc.Seal is not null) DrawSeal(g, doc, bottomTop);
                }
                else
                {
                    Text(g, RightEdge, after + 6, "continued …", 7.5, color: Grey, right: true);
                }
                var who = !string.IsNullOrWhiteSpace(doc.PreparedBy) ? doc.PreparedBy.Trim() : doc.Printed?.User;
                Line(g, Left, FooterTop, RightEdge, FooterTop);
                if (!string.IsNullOrWhiteSpace(who)) Text(g, Left, FooterTop + 5, "Prepared by " + who, 6.5, color: Label);
                var note = string.IsNullOrWhiteSpace(doc.Company.FooterText) ? "This is a Computer Generated Document" : doc.Company.FooterText.Trim();
                Text(g, PageW / 2 - g.MeasureString(note, F(6.5)).Width / 2, FooterTop + 5, note, 6.5, color: Label);
                Text(g, RightEdge, FooterTop + 5, pages.Count == 1 ? "1" : $"Page {p + 1} of {pages.Count}", 6.5, color: Label, right: true);
                if (!string.IsNullOrWhiteSpace(doc.Watermark)) DrawWatermark(g, doc.Watermark);
            }
            using var ms = new MemoryStream();
            pdf.Save(ms, false);
            return ms.ToArray();
        }
    }

    // ------------------------------------------------------------------ header

    private static List<string> CompanyLines(QuotationDocument doc)
    {
        var c = doc.Company;
        var lines = c.AddressLines.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        var contact = string.Join(" · ", new[] { c.Mobile, c.Phone, c.Email }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        if (contact.Length > 0) lines.Add(contact);
        if (!string.IsNullOrWhiteSpace(c.Gstin)) lines.Add("GSTIN " + c.Gstin.Trim() + (string.IsNullOrWhiteSpace(c.StateName) ? "" : " · " + c.StateName.Trim()
                                                                                       + (string.IsNullOrWhiteSpace(c.StateCode) ? "" : " (" + c.StateCode.Trim() + ")")));
        return lines;
    }

    private List<(string Caption, List<string> Lines)> Columns(XGraphics g, QuotationDocument doc)
    {
        const double w = (Width - 40) / 3;
        List<string> Party(PdfParty p)
        {
            var l = new List<string> { "\u0001" + p.Name.Trim() };
            foreach (var a in p.AddressLines.Where(a => !string.IsNullOrWhiteSpace(a))) l.AddRange(Wrap(g, a.Trim(), F(8.5), w));
            if (!string.IsNullOrWhiteSpace(p.Gstin)) l.Add("GSTIN " + p.Gstin.Trim());
            if (!string.IsNullOrWhiteSpace(p.StateName)) l.Add(p.StateName.Trim() + (string.IsNullOrWhiteSpace(p.StateCode) ? "" : " (" + p.StateCode.Trim() + ")"));
            var contact = string.Join(" · ", new[] { p.ContactPerson, p.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            if (contact.Length > 0) l.AddRange(Wrap(g, contact, F(8.5), w));
            if (!string.IsNullOrWhiteSpace(p.Email)) l.Add(p.Email.Trim());
            return l;
        }
        var cols = new List<(string, List<string>)> { (doc.Consignee is null && doc.BuyerCaption.StartsWith("Buyer/", StringComparison.Ordinal) ? "BUYER / SHIP TO" : "BUYER", Party(doc.Buyer)) };
        cols.Add(("SHIP TO", doc.Consignee is { } c ? Party(c) : ["Same as buyer"]));
        var refs = new List<string>();
        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            refs.AddRange(Wrap(g, label + ": " + value.Trim(), F(8.5), w));
        }
        Add("Order", doc.BuyersReference + (doc.BuyersReferenceDate is { } d && doc.BuyersReference.Trim().Length > 0 ? " · " + d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : ""));
        Add("Ref", doc.OtherReferences);
        Add("Payment", doc.PaymentTerms);
        Add("Dispatch", doc.DispatchedThrough);
        Add("Destination", doc.Destination);
        Add("Delivery", doc.TermsOfDelivery);
        Add("Delivery note", doc.DeliveryNote + (doc.DeliveryNoteDate is { } dn && doc.DeliveryNote.Trim().Length > 0 ? " · " + dn.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : ""));
        Add("Dispatch doc", doc.DispatchDocNo);
        cols.Add(("REFERENCE", refs.Count > 0 ? refs : ["—"]));
        return cols;
    }

    private const double ColumnsTop = 150;

    private double HeaderBottom(XGraphics g, QuotationDocument doc)
    {
        var top = Math.Max(ColumnsTop, 70 + Step * CompanyLines(doc).Count + 22);
        return top + 14 + Step * Columns(g, doc).Max(c => c.Lines.Count);
    }

    private void DrawHeader(XGraphics g, QuotationDocument doc)
    {
        Text(g, Left, 48, doc.Company.CompanyName, 14, bold: true);
        var lines = CompanyLines(doc);
        for (var i = 0; i < lines.Count; i++) Text(g, Left, 70 + Step * i, lines[i], 8, color: Grey);

        Text(g, RightEdge, 44, Spaced(doc.Title), 22, color: Ink, right: true);
        Text(g, RightEdge, 76, "No. " + doc.Number, 9.5, bold: true, right: true);
        var validity = doc.Kind == DocumentType.Quotation && doc.Company.QuotationValidityDays > 0 ? $" · valid {doc.Company.QuotationValidityDays} days" : "";
        Text(g, RightEdge, 90, doc.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) + validity, 8.5, color: Grey, right: true);

        var top = Math.Max(ColumnsTop, 70 + Step * lines.Count + 22);
        var cols = Columns(g, doc);
        var colW = (Width - 40) / 3;
        for (var c = 0; c < cols.Count; c++)
        {
            var x = Left + c * (colW + 20);
            Text(g, x, top, cols[c].Caption, 6.8, color: Label);
            for (var i = 0; i < cols[c].Lines.Count; i++)
            {
                var t = cols[c].Lines[i];
                var bold = t.StartsWith('\u0001');
                Text(g, x, top + 14 + Step * i, bold ? t[1..] : t, 8.5, bold);
            }
        }
    }

    /// <summary>"QUOTATION" → "Q U O T A T I O N" (letter-spaced, like the design).</summary>
    private static string Spaced(string s) => string.Join(" ", s.Trim().ToUpperInvariant().Where(ch => ch != ' ').Select(ch => ch.ToString()));

    // ------------------------------------------------------------------ items

    private sealed record Row(PdfLine Line, List<string> Names, List<string> Details, double Height);

    private Row MakeRow(XGraphics g, QuotationDocument doc, PdfLine l)
    {
        var names = Wrap(g, l.ItemName, F(8.5, true), ItemWidth);
        var details = new List<string>();
        if (doc.ShowPartNumber && l.PartNumber.Length > 0) details.Add("Part No. " + l.PartNumber);
        foreach (var d in l.DescriptionLines) details.AddRange(Wrap(g, d, F(7.5), ItemWidth));
        return new Row(l, names, details, 12 + Step * names.Count + 9.5 * details.Count);
    }

    private double DrawTable(XGraphics g, QuotationDocument doc, List<Row> rows, double top)
    {
        var showDisc = doc.Lines.Any(l => l.DiscountPercent != 0);
        void Head(double x, string t, bool right = false) => Text(g, x, top, t, 6.8, color: Label, right: right);
        Head(NoX, "#");
        Head(ItemX, "ITEM");
        Head(HsnX, "HSN");
        Head(GstRight, "GST", true);
        Head(QtyRight, "QTY", true);
        Head(RateRight, "RATE", true);
        if (showDisc) Head(DiscRight, "DISC", true);
        Head(AmountRight, "AMOUNT", true);
        g.DrawLine(new XPen(Ink, 0.8), Left, top + 13, RightEdge, top + 13);
        var y = top + 22;
        foreach (var r in rows)
        {
            var l = r.Line;
            Text(g, NoX, y, l.SlNo.ToString("00", CultureInfo.InvariantCulture), 8, color: Grey);
            for (var i = 0; i < r.Names.Count; i++) Text(g, ItemX, y + Step * i, r.Names[i], 8.5, true);
            for (var i = 0; i < r.Details.Count; i++)
            {
                var d = r.Details[i];
                Text(g, ItemX, y + Step * r.Names.Count + 9.5 * i, d, 7.5, color: d.StartsWith("Delivery: ", StringComparison.Ordinal) ? Ink : Grey);
            }
            Text(g, HsnX, y, l.Hsn, 8, color: Grey);
            Text(g, GstRight, y, l.GstRate is { } gst ? gst.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "", 8, right: true);
            Text(g, QtyRight, y, l.Quantity.ToString("0.###", CultureInfo.InvariantCulture) + " " + l.Unit, 8, right: true);
            Text(g, RateRight, y, IndianFormat.Amount(l.Rate), 8, right: true);
            if (showDisc && l.DiscountPercent != 0) Text(g, DiscRight, y, l.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%", 8, right: true);
            Text(g, AmountRight, y, IndianFormat.Amount(l.Amount), 8.5, right: true);
            y += r.Height;
            Line(g, Left, y - 6, RightEdge, y - 6);
        }
        return y;
    }

    // ------------------------------------------------------------------ totals

    private static List<(string Label, string Value)> Totals(QuotationDocument doc)
    {
        var list = new List<(string, string)> { ("Sub total", IndianFormat.Amount(doc.Subtotal)) };
        foreach (var r in doc.SummaryRows)
            list.Add((r.Label + (string.IsNullOrWhiteSpace(r.RateText) ? "" : " " + r.RateText), (r.Less ? "(-)" : "") + IndianFormat.Amount(Math.Abs(r.Amount))));
        return list;
    }

    private void DrawTotals(XGraphics g, QuotationDocument doc, List<(string Label, string Value)> totals, double top)
    {
        const double x = 330;
        var y = top;
        foreach (var (label, value) in totals)
        {
            Text(g, x, y, label, 8.5, color: Grey);
            Text(g, RightEdge, y, value, 8.5, right: true);
            y += 16;
        }
        g.DrawLine(new XPen(Ink, 0.8), x, y - 2, RightEdge, y - 2);
        Text(g, x, y + 5, "Total", 12, true);
        var total = doc.CurrencySymbol + " " + IndianFormat.Amount(doc.GrandTotal);
        Text(g, RightEdge, y + 5, total, 12, true, right: true);
        y += 26;
        if (!string.IsNullOrWhiteSpace(doc.AmountInWords))
        {
            Text(g, RightEdge, y, doc.AmountInWords, 7.5, color: Grey, right: true);
            y += 14;
        }
        if (!string.IsNullOrWhiteSpace(doc.Remarks))
        {
            y += 8;
            foreach (var l in Wrap(g, "Remarks: " + doc.Remarks.Trim(), F(8.5, true), Width))
            {
                Text(g, Left, y, l, 8.5, true);
                y += Step;
            }
        }
    }

    // ------------------------------------------------------------------ bottom: terms, bank, signature

    private sealed record Bottom(List<string> Terms, List<string> Bank, double Height);

    private Bottom BottomBlock(XGraphics g, QuotationDocument doc)
    {
        const double w = (Width - 40) / 3 * 1.3;
        var terms = doc.TermsAndConditions.Where(t => !string.IsNullOrWhiteSpace(t)).SelectMany(t => Wrap(g, t.Trim(), F(7.5), w)).ToList();
        if (doc.Kind == DocumentType.Quotation && !string.IsNullOrWhiteSpace(doc.Company.ValidityText) && doc.Company.QuotationValidityDays > 0)
            terms.AddRange(Wrap(g, doc.Company.ValidityText.Replace("{days}", doc.Company.QuotationValidityDays.ToString(CultureInfo.InvariantCulture)), F(7.5), w));
        var c = doc.Company;
        var bank = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.BankName)) bank.Add(c.BankName.Trim());
        if (!string.IsNullOrWhiteSpace(c.BankAccountNumber)) bank.Add("A/c " + c.BankAccountNumber.Trim());
        if (!string.IsNullOrWhiteSpace(c.BankIfsc)) bank.Add("IFSC " + c.BankIfsc.Trim());
        if (!string.IsNullOrWhiteSpace(c.BankBranch)) bank.Add(c.BankBranch.Trim());
        var ids = string.Join(" · ", new[] { (c.Pan, "PAN "), (c.MsmeNumber, "MSME "), (c.Iec, "IEC ") }
            .Where(p => !string.IsNullOrWhiteSpace(p.Item1)).Select(p => p.Item2 + p.Item1.Trim()));
        if (ids.Length > 0) bank.AddRange(Wrap(g, ids, F(7.5), (Width - 40) / 3));
        var lines = Math.Max(Math.Max(terms.Count, bank.Count), 5);
        return new Bottom(terms, bank, 14 + 10 * lines);
    }

    private void DrawBottom(XGraphics g, QuotationDocument doc, Bottom b, double top)
    {
        var colW = (Width - 40) / 3;
        var termsX = Left;
        var bankX = Left + colW * 1.3 + 20;
        Text(g, termsX, top, doc.TermsAndConditions.Count > 0 ? "TERMS" : "", 6.8, color: Label);
        for (var i = 0; i < b.Terms.Count; i++) Text(g, termsX, top + 14 + 10 * i, b.Terms[i], 7.5, color: Grey);
        if (b.Bank.Count > 0) Text(g, bankX, top, "BANK", 6.8, color: Label);
        for (var i = 0; i < b.Bank.Count; i++) Text(g, bankX, top + 14 + 10 * i, b.Bank[i], 7.5, color: Grey);
        Text(g, RightEdge, top, "FOR " + doc.Company.CompanyName.ToUpperInvariant(), 6.8, color: Label, right: true);
        var sign = string.IsNullOrWhiteSpace(doc.Company.AuthorisedSignatoryLabel) ? "Authorised Signatory" : doc.Company.AuthorisedSignatoryLabel.Trim();
        Text(g, RightEdge, top + b.Height - 14, sign, 8.5, true, right: true);
    }

    private void DrawSeal(XGraphics g, QuotationDocument doc, double bottomTop)
    {
        try
        {
            using var ms = new MemoryStream(doc.Seal!);
            using var img = XImage.FromStream(ms);
            var width = Math.Clamp(doc.Company.SealWidth, 40, 260);
            var height = width * img.PixelHeight / Math.Max(1, img.PixelWidth);
            var x = Math.Clamp(RightEdge - 70 - width / 2 + doc.Company.SealOffsetX, 0, PageW - width);
            var y = Math.Clamp(bottomTop + 30 - height / 2 + doc.Company.SealOffsetY, 0, PageH - height);
            g.DrawImage(img, x, y, width, height);
        }
        catch (Exception)
        {
            // A picture that cannot be read: the PDF without the seal.
        }
    }

    private void DrawWatermark(XGraphics g, string text)
    {
        var state = g.Save();
        g.TranslateTransform(PageW / 2, PageH / 2);
        g.RotateTransform(-35);
        var f = F(90, true);
        var w = g.MeasureString(text, f).Width;
        g.DrawString(text, f, new XSolidBrush(XColor.FromArgb(28, 0, 0, 0)), -w / 2, 30);
        g.Restore(state);
    }

    // ------------------------------------------------------------------ helpers

    private void Text(XGraphics g, double x, double t, string? text, double size, bool bold = false, XColor? color = null, bool right = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = F(size, bold);
        var left = right ? x - g.MeasureString(text, font).Width : x;
        g.DrawString(text, font, new XSolidBrush(color ?? Ink), left, t + 0.85 * size, XStringFormats.BaseLineLeft);
    }

    private static void Line(XGraphics g, double x1, double y, double x2, double y2) => g.DrawLine(new XPen(Hair, 0.7), x1, y, x2, y2);

    private static List<string> Wrap(XGraphics g, string? text, XFont font, double width) => QuotationPdfRenderer.Wrap(g, text, font, width);
}
