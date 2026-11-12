using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Quotation.Core.Calculation;

namespace Quotation.Pdf;

/// <summary>
/// The "Modern" layout (the Z button): T.SAIFUDDIN &amp; CO's own design from docs/templates/modern-z/SPEC.md — logo banner,
/// maroon corners, logo watermark, dark table header, totals on the right, terms and sign-off at the bottom.
/// Positions are the spec's, in points from the top-left corner; text "T" values are tops (baseline = T + 0.85 × size).
/// Beyond the spec: HSN/SAC and Disc % columns, wrapped names and addresses, the consignee, and further pages when the
/// items do not fit on one.
/// </summary>
public sealed class ModernPdfRenderer
{
    // ---- page and colours (SPEC.md §2, §3) ----
    private const double PageW = QuotationPdfRenderer.A4Width;
    private const double PageH = QuotationPdfRenderer.A4Height;
    private static readonly XColor Ink = XColor.FromArgb(0x33, 0x33, 0x33);
    private static readonly XColor Black = XColors.Black;
    private static readonly XColor White = XColors.White;
    private static readonly XColor Maroon = XColor.FromArgb(0x80, 0x0A, 0x08);
    private static readonly XColor Dark = XColor.FromArgb(0x1C, 0x20, 0x1F);
    private static readonly XColor TableHead = XColor.FromArgb(0x3C, 0x3D, 0x3A);
    private static readonly XColor Rule = XColor.FromArgb(0xAD, 0xAD, 0xAD);
    private static readonly XColor PageNo = XColor.FromArgb(0xAA, 0xAA, 0xAA);

    // ---- table columns ----
    private const double TableX = 45.6, TableW = 515;
    private const double SlX = 57.8, SlHeadX = 50.9, ItemX = 86.3, HsnX = 266, GstRight = 346, QtyRight = 386, RateRight = 446, DiscRight = 486, AmountRight = 553.1;
    private const double ItemWidth = HsnX - ItemX - 8;
    private const double TotalsLabelRight = 451.8;
    private const double LeftX = 46.4;
    private const double LineStep = 9.22;
    private const double FirstHeadTop = 328.8, NextHeadTop = 130, HeadHeight = 24;
    /// <summary>Rows end above the footer rule on pages that continue.</summary>
    private const double ContinuedLimit = 772;

    private readonly Dictionary<(double, bool), XFont> _fonts = [];
    private readonly Lock _renderLock = new();

    public ModernPdfRenderer() => PdfFonts.EnsureInstalled();

    private XFont F(double size, bool bold = false)
    {
        if (!_fonts.TryGetValue((size, bold), out var f))
        {
            _fonts[(size, bold)] = f = new XFont(PdfFonts.Ubuntu, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        }
        return f;
    }

    public byte[] Render(QuotationDocument doc)
    {
        lock (_renderLock)
        {
            using var pdf = new PdfDocument();
            pdf.Info.Title = $"{doc.ModernTitle} {doc.Number}";
            pdf.Info.Author = doc.Company.CompanyName;
            pdf.Info.Creator = "Quotations";
            pdf.Info.Subject = $"{doc.ModernTitle} {doc.Number} — {doc.Buyer.Name}";

            var measurePage = new PdfDocument().AddPage();
            using var measure = XGraphics.FromPdfPage(measurePage);
            var layout = Layout(measure, doc);
            for (var p = 0; p < layout.Pages.Count; p++)
            {
                var page = pdf.AddPage();
                page.Width = XUnit.FromPoint(PageW);
                page.Height = XUnit.FromPoint(PageH);
                using var g = XGraphics.FromPdfPage(page);
                DrawPage(g, doc, layout, p);
            }
            using var ms = new MemoryStream();
            pdf.Save(ms, false);
            return ms.ToArray();
        }
    }

    // =====================================================================================
    // Layout
    // =====================================================================================

    private sealed record Row(PdfLine Line, List<string> NameLines, List<string> Details, double Height);

    private sealed record TextLine(string Text, double Size, bool Bold);

    private sealed record PageLayout(
        List<List<Row>> Pages, double FirstHeadTop, List<(string Label, string Value, bool Bold)> Totals, List<TextLine> Bottom, double BottomTop);

    private PageLayout Layout(XGraphics g, QuotationDocument doc)
    {
        var headTop = Math.Max(FirstHeadTop, HeaderBottom(g, doc) + 22);
        var rows = doc.Lines.Select(l => MakeRow(g, doc, l)).ToList();
        var totals = Totals(doc);
        var totalsHeight = 9.2 + 25.4 * (totals.Count - 1) + 14 + 10; // and "E. & O.E" under the total
        var (bottom, bottomTop) = BottomBlock(g, doc);
        var lastLimit = bottomTop - 12 - totalsHeight;

        var pages = new List<List<Row>> { new() };
        var used = 0.0;
        double Start(int page) => (page == 0 ? headTop : NextHeadTop) + HeadHeight;
        foreach (var row in rows)
        {
            if (Start(pages.Count - 1) + used + row.Height > ContinuedLimit && pages[^1].Count > 0)
            {
                pages.Add([]);
                used = 0;
            }
            pages[^1].Add(row);
            used += row.Height;
        }
        // The last page also needs the totals, terms and sign-off: carry rows over until they fit.
        while (Start(pages.Count - 1) + pages[^1].Sum(r => r.Height) > lastLimit && pages[^1].Count > 0)
        {
            var carry = new List<Row>();
            var last = pages[^1];
            while (last.Count > 0 && Start(pages.Count - 1) + last.Sum(r => r.Height) > lastLimit)
            {
                carry.Insert(0, last[^1]);
                last.RemoveAt(last.Count - 1);
            }
            if (last.Count == 0)
            {
                // Not even one row fits with the totals: the rows stay, the totals go on a page of their own.
                last.AddRange(carry);
                pages.Add([]);
                break;
            }
            pages.Add(carry);
            if (carry.Count == 1 && Start(pages.Count - 1) + carry[0].Height > lastLimit) break;
        }
        return new PageLayout(pages, headTop, totals, bottom, bottomTop);
    }

    private Row MakeRow(XGraphics g, QuotationDocument doc, PdfLine line)
    {
        var names = Wrap(g, line.ItemName, F(9), ItemWidth);
        if (names.Count == 0) names.Add("");
        var details = new List<string>();
        if (doc.ShowPartNumber && line.PartNumber.Length > 0) details.Add("Part No. " + line.PartNumber);
        foreach (var d in line.DescriptionLines) details.AddRange(Wrap(g, d, F(7), ItemWidth));
        var last = 18.9 + 10.8 * (names.Count - 1) + 12.3 * (Math.Max(details.Count, 1) - 1);
        return new Row(line, names, details, last + 14.9 + 0.8);
    }

    private static List<(string Label, string Value, bool Bold)> Totals(QuotationDocument doc)
    {
        var rows = new List<(string, string, bool)> { ("Sub Total", Money(doc.Subtotal), false) };
        foreach (var s in doc.SummaryRows.Where(s => s.Label.StartsWith("Packing", StringComparison.OrdinalIgnoreCase)))
        {
            rows.Add((s.Label, Money(s.Amount), false));
        }
        foreach (var t in doc.TaxLines.Where(t => t.Amount != 0))
        {
            var rate = t.RatePercent.ToString("0.##", CultureInfo.InvariantCulture);
            rows.Add(($"{t.Name}{rate} ({rate}%)", Money(t.Amount), false));
        }
        foreach (var s in doc.SummaryRows.Where(s => s.Label.StartsWith("Round", StringComparison.OrdinalIgnoreCase) && s.Amount != 0))
        {
            rows.Add(("Round Off", (s.Amount < 0 ? "(-)" : "") + Money(Math.Abs(s.Amount)), false));
        }
        rows.Add(("Total", "₹" + Money(doc.GrandTotal), true));
        return rows;
    }

    /// <summary>
    /// The foot of the last page, stacked upward from just above the footer rule: the company's PAN / MSME / IEC (left) and
    /// bank details (right) on the last lines, terms &amp; conditions and remarks above them, and the "Pay by UPI" QR above the
    /// bank details.
    /// </summary>
    private (List<TextLine> Lines, double Top) BottomBlock(XGraphics g, QuotationDocument doc)
    {
        var hasQr = UpiQr.LinkFor(doc) is not null;
        var c = doc.Company;
        var ids = new List<(string Label, string Value)>();
        if (doc.Kind != Quotation.Core.Domain.DocumentType.ProformaInvoice && !string.IsNullOrWhiteSpace(c.Pan)) ids.Add(("Company's PAN", c.Pan.Trim()));
        if (!string.IsNullOrWhiteSpace(c.MsmeNumber)) ids.Add(("Company's MSME No", c.MsmeNumber.Trim()));
        if (!string.IsNullOrWhiteSpace(c.Iec)) ids.Add(("Company's IEC", c.Iec.Trim()));
        var bank = new List<(string Label, string Value)>();
        if (!string.IsNullOrWhiteSpace(c.BankName) || !string.IsNullOrWhiteSpace(c.BankAccountNumber))
        {
            bank.Add(("A/c Holder's Name", (c.BankAccountHolder ?? "").Trim()));
            bank.Add(("Bank Name", (c.BankName ?? "").Trim()));
            bank.Add(("A/c No.", (c.BankAccountNumber ?? "").Trim()));
            bank.Add(("Branch & IFS Code", string.Join(" & ", new[] { c.BankBranch, c.BankIfsc }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()))));
        }

        var bottom = FootBottom;
        _pairs = [];
        var pairRows = Math.Max(ids.Count, bank.Count);
        if (pairRows > 0)
        {
            var labelFont = F(PairSize);
            var valueFont = F(PairSize, true);
            var idsRight = LeftX;
            if (ids.Count > 0)
            {
                var labelW = ids.Max(p => g.MeasureString(p.Label, labelFont).Width);
                var valueW = ids.Max(p => g.MeasureString(p.Value, valueFont).Width);
                idsRight = LeftX + labelW + PairGap + valueW;
                var top = bottom - PairStep * (ids.Count - 1);
                for (var i = 0; i < ids.Count; i++) _pairs.Add((LeftX, top + PairStep * i, ids[i].Label, ids[i].Value, labelW, PairSize));
            }
            if (bank.Count > 0)
            {
                // In the middle: between the company's numbers and the QR (or the right margin); slightly smaller text when
                // long bank details would otherwise run into the UPI ID.
                var from = idsRight + 16;
                var to = hasQr ? AmountRight - QrTextWidth(g, doc) - 20 : AmountRight;
                double size = PairSize, labelW, blockW;
                while (true)
                {
                    var lf = F(size);
                    var vf = F(size, true);
                    labelW = bank.Max(p => g.MeasureString(p.Label, lf).Width);
                    blockW = labelW + PairGap + bank.Max(p => g.MeasureString(p.Value, vf).Width);
                    if (blockW <= to - from || size <= 6.5) break;
                    size -= 0.25;
                }
                var x = Math.Max(from, from + (to - from - blockW) / 2);
                var top = bottom - PairStep * (bank.Count - 1);
                for (var i = 0; i < bank.Count; i++) _pairs.Add((x, top + PairStep * i + (PairSize - size) * 0.85, bank[i].Label, bank[i].Value, labelW, size));
            }
            bottom = bottom - PairStep * (pairRows - 1) - 18;
        }
        else
        {
            bottom -= 21;
        }

        var placed = new List<(double T, TextLine Line)>();

        // Terms and remarks above them; with the "Pay by UPI" QR at the right, the terms keep clear of it.
        var width = hasQr ? 500 - QrSize - 20 : 500;
        var terms = new List<string>();
        for (var i = 0; i < doc.TermsAndConditions.Count; i++)
        {
            terms.AddRange(Wrap(g, $"{i + 1}. {doc.TermsAndConditions[i]}", F(8), width));
        }
        var remarks = string.IsNullOrWhiteSpace(doc.Remarks) ? [] : Wrap(g, "Remarks: " + doc.Remarks, F(8, true), width);

        // Lines from the bottom up; each item is (T, line).
        if (terms.Count > 0)
        {
            var top = bottom - 10.5 * (terms.Count - 1);
            for (var i = 0; i < terms.Count; i++) placed.Add((top + 10.5 * i, new TextLine(terms[i], 8, false)));
            placed.Add((top - 14, new TextLine("Terms & Conditions", 10, false)));
            bottom = top - 14 - 16;
        }
        if (remarks.Count > 0)
        {
            var top = bottom - 10.5 * (remarks.Count - 1);
            for (var i = 0; i < remarks.Count; i++) placed.Add((top + 10.5 * i, new TextLine(remarks[i], 8, true)));
        }
        _bottomPositions = placed;
        var blockTop = Math.Min(placed.Count == 0 ? FootBottom : placed.Min(p => p.T), _pairs.Count == 0 ? FootBottom : _pairs.Min(p => p.T));
        if (hasQr) blockTop = Math.Min(blockTop, QrTop);
        return (placed.Select(p => p.Line).ToList(), blockTop);
    }

    private const double PairSize = 7.5, PairStep = 10.5, PairGap = 12;

    private List<(double X, double T, string Label, string Value, double LabelWidth, double Size)> _pairs = [];

    /// <summary>"Company's PAN : AAAFT0000A" — the label, a colon in line with the others and the value in bold.</summary>
    private void DrawPairs(XGraphics g)
    {
        foreach (var (x, t, label, value, labelWidth, size) in _pairs)
        {
            Text(g, x, t, label, size);
            Text(g, x + labelWidth + 4, t, ":", size);
            Text(g, x + labelWidth + PairGap, t, value, size, true, Black);
        }
    }

    /// <summary>"Pay by UPI" QR code at the bottom right of the last page.</summary>
    private const double QrSize = 57.6; // 10 % smaller than the first 64
    private const double QrBottom = FootBottom;
    /// <summary>The top of the last line above the footer rule (791.3).</summary>
    private const double FootBottom = 778;
    /// <summary>The "UPI ID" line is the last line, level with the last bank-detail line.</summary>
    private const double QrTop = QrBottom - 11 - 3 - QrSize - 3;

    /// <summary>The width of the QR block: the code or the "UPI ID: …" line under it, whichever is wider.</summary>
    private double QrTextWidth(XGraphics g, QuotationDocument doc) =>
        Math.Max(QrSize, Math.Min(160, g.MeasureString("UPI ID: " + doc.Company.UpiId.Trim(), F(PairSize)).Width));

    private void DrawUpiQr(XGraphics g, QuotationDocument doc)
    {
        if (UpiQr.LinkFor(doc) is not { } link) return;
        var right = AmountRight;
        var t = QrTop;
        Text(g, right, t, "Pay by UPI", 9, color: Black, align: Right);
        t += 11 + 3;
        UpiQr.Draw(g, link, right - QrSize, t, QrSize);
        t += QrSize + 3;
        TextFit(g, right, t, "UPI ID: " + doc.Company.UpiId.Trim(), PairSize, 160);
    }

    private List<(double T, TextLine Line)> _bottomPositions = [];

    // =====================================================================================
    // Header (first page)
    // =====================================================================================

    private static List<string> CompanyLines(QuotationDocument doc)
    {
        var c = doc.Company;
        var lines = c.AddressLines.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (!string.IsNullOrWhiteSpace(c.Mobile)) lines.Add(c.Mobile);
        if (!string.IsNullOrWhiteSpace(c.Phone)) lines.Add(c.Phone);
        if (!string.IsNullOrWhiteSpace(c.Email)) lines.Add(c.Email);
        if (!string.IsNullOrWhiteSpace(c.Gstin)) lines.Add("GSTIN " + c.Gstin);
        return lines;
    }

    /// <summary>Address lines (at most three, smaller when needed, never cut) and then GSTIN, state and contact, with their sizes.</summary>
    private List<(string Text, double Size)> PartyLines(XGraphics g, PdfParty p, double width)
    {
        var lines = new List<(string, double)>();
        var address = p.AddressLines.SelectMany(a => Wrap(g, a, F(8), width)).ToList();
        if (address.Count <= QuotationPdfRenderer.MaxAddressLines)
        {
            lines.AddRange(address.Select(a => (a, 8.0)));
        }
        else
        {
            var joined = QuotationPdfRenderer.JoinAddress(p.AddressLines);
            for (var size = 8.0; ; size -= 0.5)
            {
                var wrapped = Wrap(g, joined, F(size), width);
                if (wrapped.Count > QuotationPdfRenderer.MaxAddressLines && size - 0.5 >= QuotationPdfRenderer.MinAddressSize) continue;
                lines.AddRange(wrapped.Select(l => (l, size)));
                break;
            }
        }
        if (!string.IsNullOrWhiteSpace(p.StateName))
            lines.Add((p.StateName + (string.IsNullOrWhiteSpace(p.StateCode) ? "" : $", Code {p.StateCode}"), 8));
        if (!string.IsNullOrWhiteSpace(p.Gstin)) lines.Add(("GSTIN " + p.Gstin, 8));
        if (!string.IsNullOrWhiteSpace(p.Pan)) lines.Add(("PAN " + p.Pan, 8));
        var contact = string.Join(", ", new[] { p.ContactPerson, p.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (contact.Length > 0) lines.Add((contact, 8));
        if (!string.IsNullOrWhiteSpace(p.Email)) lines.Add((p.Email, 8));
        return lines;
    }

    private double PartyWidth(QuotationDocument doc) => doc.Consignee is null ? 330 : 165;

    private double BuyerTop(QuotationDocument doc) =>
        Math.Max(227.8, 148.2 + LineStep * (CompanyLines(doc).Count - 1) + 24);

    /// <summary>The top of the lowest line of the buyer/consignee blocks and the date/terms.</summary>
    private double HeaderBottom(XGraphics g, QuotationDocument doc)
    {
        var top = BuyerTop(doc);
        var w = PartyWidth(doc);
        var name = Wrap(g, doc.Buyer.Name, F(9, true), w).Count;
        var buyer = top + 21.9 + 10.5 * (name - 1) + LineStep * Math.Max(0, PartyLines(g, doc.Buyer, w).Count - 1);
        var consignee = doc.Consignee is null ? 0
            : top + 21.9 + 10.5 * (Wrap(g, doc.Consignee.Name, F(9, true), w).Count - 1) + LineStep * Math.Max(0, PartyLines(g, doc.Consignee, w).Count - 1);
        var dateBlock = top + (string.IsNullOrWhiteSpace(doc.PaymentTerms) ? 53.2 : 72.3);
        return Math.Max(Math.Max(buyer, consignee), dateBlock);
    }

    private void DrawHeader(XGraphics g, QuotationDocument doc)
    {
        // Seller (top left).
        Text(g, LeftX, 137.7, doc.Company.CompanyName, 9, true);
        var lines = CompanyLines(doc);
        for (var i = 0; i < lines.Count; i++) Text(g, LeftX, 148.2 + LineStep * i, lines[i], 8);

        // Title (top right).
        Text(g, 559.9, 139.9, doc.ModernTitle, 24, align: Right);
        Text(g, 559.8, 165.5, $"Ref No. {doc.Number}", 10, true, align: Right);
        var refTop = 178.5;
        if (!string.IsNullOrWhiteSpace(doc.BuyersReference))
        {
            // The customer's PO / order number (Buyer's Ref./Order No.) and its date.
            var po = doc.BuyersReference.Trim() + (doc.BuyersReferenceDate is { } d ? " dt. " + d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "");
            Text(g, 559.8, refTop, $"Order No: {po}", 10, true, align: Right);
            refTop += 13;
        }
        if (!string.IsNullOrWhiteSpace(doc.OtherReferences)) Text(g, 559.8, refTop, $"Other Ref: {doc.OtherReferences}", 10, true, align: Right);

        // Buyer (and consignee when it is a different party).
        var top = BuyerTop(doc);
        var w = PartyWidth(doc);
        var buyerCaption = doc.Consignee is null && doc.BuyerCaption.StartsWith("Buyer/", StringComparison.Ordinal) ? "Buyer/Consignee" : "Buyer";
        DrawParty(g, buyerCaption, doc.Buyer, LeftX, top, w);
        if (doc.Consignee is not null) DrawParty(g, "Consignee", doc.Consignee, LeftX + w + 14, top, w);

        // Date and payment terms (right).
        var dateTop = top + 52.4;
        Text(g, 460.3, dateTop, "Date :", 10, align: Right);
        Text(g, 559.9, dateTop + 0.8, doc.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), 8, align: Right);
        if (!string.IsNullOrWhiteSpace(doc.PaymentTerms))
        {
            Text(g, 460.3, dateTop + 19, "Terms :", 10, align: Right);
            Text(g, 559.8, dateTop + 19.9, doc.PaymentTerms, 8, align: Right);
        }
    }

    private void DrawParty(XGraphics g, string caption, PdfParty p, double x, double top, double width)
    {
        Text(g, x, top, caption, 10);
        var y = top + 11.4;
        foreach (var n in Wrap(g, p.Name, F(9, true), width))
        {
            Text(g, x, y, n, 9, true);
            y += 10.5;
        }
        foreach (var (text, size) in PartyLines(g, p, width))
        {
            Text(g, x, y + (8 - size) * 0.85, text, size);
            y += LineStep;
        }
    }

    // =====================================================================================
    // Pages
    // =====================================================================================

    private void DrawPage(XGraphics g, QuotationDocument doc, PageLayout layout, int index)
    {
        var lastPage = index == layout.Pages.Count - 1;
        DrawArtwork(g);
        double headTop;
        if (index == 0)
        {
            DrawHeader(g, doc);
            headTop = layout.FirstHeadTop;
        }
        else
        {
            Text(g, 559.9, 60, doc.ModernTitle, 18, align: Right);
            Text(g, 559.8, 82, $"Ref No. {doc.Number}", 10, true, align: Right);
            headTop = NextHeadTop;
        }

        // Table header band.
        Fill(g, TableHead, TableX, headTop, TableW, HeadHeight);
        Text(g, SlHeadX, headTop + 8.1, "S.No", 9, color: White);
        Text(g, ItemX, headTop + 8.1, "Item & Description", 9, color: White);
        Text(g, HsnX, headTop + 8.1, "HSN/SAC", 9, color: White);
        Text(g, GstRight, headTop + 8.1, "GST", 9, color: White, align: Right);
        Text(g, QtyRight, headTop + 8.1, "Qty", 9, color: White, align: Right);
        Text(g, RateRight, headTop + 8.1, "Rate", 9, color: White, align: Right);
        Text(g, DiscRight, headTop + 8.1, "Disc %", 9, color: White, align: Right);
        Text(g, AmountRight, headTop + 8.1, "Amount", 9, color: White, align: Right);

        var rowTop = headTop + HeadHeight;
        foreach (var row in layout.Pages[index]) rowTop = DrawRow(g, row, rowTop);

        if (lastPage)
        {
            DrawTotals(g, layout.Totals, rowTop);
            foreach (var (t, line) in _bottomPositions) Text(g, LeftX, t, line.Text, line.Size, line.Bold);
            DrawPairs(g);
            DrawUpiQr(g, doc);
            if (doc.Seal is not null) DrawSeal(g, doc);
        }
        else
        {
            Text(g, AmountRight, 778, "continued ...", 7, align: Right);
        }

        if (doc.Printed is { } p)
        {
            Text(g, LeftX, 801, $"Printed on {p.LocalTime.ToString("dd-MMM-yy", CultureInfo.InvariantCulture)} at " +
                $"{p.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)}   By: {p.User}", 6, color: PageNo);
        }
        var pageText = layout.Pages.Count == 1
            ? "1"
            : $"Page {(index + 1).ToString(CultureInfo.InvariantCulture)} of {layout.Pages.Count.ToString(CultureInfo.InvariantCulture)}";
        Text(g, 562.8, 801, pageText, 6, color: PageNo, align: Right);
        var note = "This is a Computer Generated Document";
        Text(g, PageW / 2 - g.MeasureString(note, F(6)).Width / 2, 801, note, 6, color: PageNo);
        if (!string.IsNullOrWhiteSpace(doc.Watermark)) DrawWatermark(g, doc.Watermark);
    }

    /// <summary>The fixed artwork of every page (SPEC §4), in its stacking order.</summary>
    private static void DrawArtwork(XGraphics g)
    {
        SvgArt.Draw(g, "logo-square.svg", 83, 212, 430, 393.4, opacity: 0.065);
        Fill(g, Dark, 0, 0, PageW, 17);
        Polygon(g, Maroon, (494.5, 17), (PageW, 17), (PageW, 44), (525, 44));
        Polygon(g, Maroon, (0, PageH - 28.7), (0, PageH), (82, PageH));
        Fill(g, Dark, 0, PageH - 3.1, PageW, 3.1);
        Fill(g, Rule, 39.6, 791.3, 527, 0.7);
        SvgArt.Draw(g, "logo-banner.svg", 14, 46, 264.15, 70);
    }

    private double DrawRow(XGraphics g, Row row, double rowTop)
    {
        var l = row.Line;
        var y = rowTop + 8.8;
        Text(g, SlX, y, l.SlNo.ToString(CultureInfo.InvariantCulture), 9, color: Black);
        for (var i = 0; i < row.NameLines.Count; i++) Text(g, ItemX, y + 10.8 * i, row.NameLines[i], 9, color: Black);
        Text(g, HsnX, y, l.Hsn, 9, color: Black);
        if (l.GstRate is { } gst) Text(g, GstRight, y, gst.ToString("0.##", CultureInfo.InvariantCulture) + "%", 9, color: Black, align: Right);
        TextFit(g, QtyRight, y, IndianFormat.Amount(l.Quantity), 9, QtyRight - GstRight - 6);
        Text(g, QtyRight, rowTop + 18.9, l.Unit, 7, align: Right);
        TextFit(g, RateRight, y, Money(l.Rate), 9, RateRight - QtyRight - 6);
        if (l.DiscountPercent != 0)
            Text(g, DiscRight, y, l.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%", 9, color: Black, align: Right);
        TextFit(g, AmountRight, y, Money(l.Amount), 9, AmountRight - DiscRight - 6);
        var detailTop = rowTop + 18.9 + 10.8 * (row.NameLines.Count - 1);
        for (var i = 0; i < row.Details.Count; i++) Text(g, ItemX, detailTop + 12.3 * i, row.Details[i], 7);
        var ruleY = rowTop + row.Height - 0.8;
        Fill(g, Rule, TableX, ruleY, TableW, 0.8);
        return rowTop + row.Height;
    }

    private void DrawTotals(XGraphics g, List<(string Label, string Value, bool Bold)> totals, double rowTop)
    {
        var y = rowTop + 9.2;
        for (var i = 0; i < totals.Count; i++)
        {
            var (label, value, bold) = totals[i];
            Text(g, TotalsLabelRight, y, label, 9, bold, Black, Right);
            TextFit(g, AmountRight, y, value, 9, AmountRight - TotalsLabelRight - 8, bold);
            y += 25.4;
        }
        Text(g, AmountRight, y - 25.4 + 14, "E. & O.E", 7, color: PageNo, align: Right);
    }

    private void DrawSeal(XGraphics g, QuotationDocument doc)
    {
        try
        {
            using var ms = new MemoryStream(doc.Seal!);
            using var img = XImage.FromStream(ms);
            var width = Math.Clamp(doc.Company.SealWidth, 40, 260);
            var height = width * img.PointHeight / Math.Max(1, img.PointWidth);
            // At the foot of the page, between the company's numbers and the bank details, like a stamp.
            var x = Math.Clamp(230 - width / 2 + doc.Company.SealOffsetX, 0, PageW - width);
            var y = Math.Clamp(756 - height / 2 + doc.Company.SealOffsetY, 0, PageH - height);
            g.DrawImage(img, x, y, width, height);
        }
        catch (Exception)
        {
            // An unreadable seal image must never prevent printing.
        }
    }

    private void DrawWatermark(XGraphics g, string text)
    {
        var state = g.Save();
        g.TranslateTransform(PageW / 2, PageH / 2);
        g.RotateTransform(-35);
        var f = F(90, true);
        var w = g.MeasureString(text, f).Width;
        g.DrawString(text, f, new XSolidBrush(XColor.FromArgb(40, 0x80, 0x0A, 0x08)), -w / 2, 30);
        g.Restore(state);
    }

    // =====================================================================================
    // Drawing helpers
    // =====================================================================================

    private const bool Right = true;

    /// <summary>Text with its top at <paramref name="t"/> (baseline = T + 0.85 × size); x is the right edge when right-aligned.</summary>
    private void Text(XGraphics g, double x, double t, string? text, double size, bool bold = false, XColor? color = null, bool align = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = F(size, bold);
        var left = align ? x - g.MeasureString(text, font).Width : x;
        g.DrawString(text, font, new XSolidBrush(color ?? Ink), left, t + 0.85 * size, XStringFormats.BaseLineLeft);
    }

    /// <summary>A right-aligned value that must not be cut: a slightly smaller size when wider than <paramref name="width"/>.</summary>
    private void TextFit(XGraphics g, double right, double t, string text, double size, double width, bool bold = false)
    {
        var s = size;
        while (s > 6 && g.MeasureString(text, F(s, bold)).Width > width) s -= 0.5;
        Text(g, right, t + (size - s) * 0.85, text, s, bold, Black, Right);
    }

    private static void Fill(XGraphics g, XColor colour, double x, double y, double w, double h) =>
        g.DrawRectangle(new XSolidBrush(colour), x, y, w, h);

    private static void Polygon(XGraphics g, XColor colour, params (double X, double Y)[] points) =>
        g.DrawPolygon(new XSolidBrush(colour), points.Select(p => new XPoint(p.X, p.Y)).ToArray(), XFillMode.Winding);

    private static string Money(decimal value) => IndianFormat.Amount(value);

    private static List<string> Wrap(XGraphics g, string? text, XFont font, double width) => QuotationPdfRenderer.Wrap(g, text, font, width);
}
