using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;

namespace Quotation.Pdf;

/// <summary>
/// Renders a quotation or proforma invoice in the layout of the TallyPrime prints: "Printed on … By: …",
/// title, boxed header with the company / consignee / buyer blocks on the left and the reference grid on the
/// right, the ruled item table (Sl No., Description of Goods, HSN/SAC, GST Rate, Due on | Part No., Quantity,
/// Rate, per, Disc. %, Amount), P&amp;F / tax / round-off rows, totals, amount in words, the HSN-wise tax table
/// (proforma invoice), PAN/MSME/IEC, declaration, bank details and the signature block.
///
/// Every text block is measured before it is placed. Item rows are never split across pages; the
/// header and column headings repeat on every page; totals, amount in words, declaration, bank details
/// and signatures are kept together on the last page.
/// </summary>
public sealed class QuotationPdfRenderer
{
    // ---- page geometry (points; A4 portrait) ----
    internal const double PageWidth = 595.28;
    internal const double PageHeight = 841.89;
    internal const double MarginLeft = 28;
    internal const double MarginRight = 28;
    internal const double MarginTop = 20;
    internal const double MarginBottom = 20;
    internal const double ContentWidth = PageWidth - MarginLeft - MarginRight;
    internal const double Pad = 3.5;

    // Column widths of the item table (Description takes the rest). Quotations show "Due on", proforma invoices "Part No.".
    private static readonly (string Title, double Width, Align Align)[] QuotationColumns =
    [
        ("Sl\nNo.", 20, Align.Center),
        ("Description of Goods", 0, Align.Left),
        ("HSN/SAC", 46, Align.Left),
        ("GST\nRate", 28, Align.Right),
        ("Due on", 36, Align.Left),
        ("Quantity", 50, Align.Right),
        ("Rate", 52, Align.Right),
        ("per", 26, Align.Left),
        ("Disc.\n%", 26, Align.Right),
        ("Amount", 64, Align.Right),
    ];

    private static readonly (string Title, double Width, Align Align)[] ProformaColumns =
    [
        ("Sl\nNo.", 20, Align.Center),
        ("Description of Goods", 0, Align.Left),
        ("HSN/SAC", 46, Align.Left),
        ("GST\nRate", 28, Align.Right),
        ("Part No.", 46, Align.Left),
        ("Quantity", 50, Align.Right),
        ("Rate", 52, Align.Right),
        ("per", 26, Align.Left),
        ("Disc.\n%", 26, Align.Right),
        ("Amount", 64, Align.Right),
    ];

    private const int ColSl = 0, ColDesc = 1, ColHsn = 2, ColGst = 3, ColRef = 4, ColQty = 5, ColRate = 6, ColPer = 7, ColDisc = 8, ColAmount = 9;

    private enum Align { Left, Center, Right }

    private readonly XFont _title, _company, _label, _text, _bold, _italic, _boldItalic, _head, _small, _words;
    private readonly Lock _renderLock = new();
    private (string Title, double Width, Align Align)[] Columns = QuotationColumns;
    private double[] _colX = [];
    private double[] _colW = [];

    public QuotationPdfRenderer()
    {
        PdfFonts.EnsureInstalled();
        _title = Font(11, XFontStyleEx.Bold);
        _company = Font(10, XFontStyleEx.Bold);
        _label = Font(7, XFontStyleEx.Regular);
        _text = Font(8, XFontStyleEx.Regular);
        _bold = Font(8, XFontStyleEx.Bold);
        _italic = Font(7.5, XFontStyleEx.Italic);
        _boldItalic = Font(8, XFontStyleEx.BoldItalic);
        _head = Font(7.5, XFontStyleEx.Bold);
        _small = Font(7, XFontStyleEx.Regular);
        _words = Font(8.5, XFontStyleEx.Bold);
        UseColumns(DocumentType.Quotation);
    }

    private void UseColumns(DocumentType kind)
    {
        Columns = kind == DocumentType.ProformaInvoice ? ProformaColumns : QuotationColumns;
        var fixedWidth = Columns.Sum(c => c.Width);
        _colW = Columns.Select(c => c.Width == 0 ? ContentWidth - fixedWidth : c.Width).ToArray();
        _colX = new double[_colW.Length];
        var x = MarginLeft;
        for (var i = 0; i < _colW.Length; i++)
        {
            _colX[i] = x;
            x += _colW[i];
        }
    }

    private static XFont Font(double size, XFontStyleEx style) => new(PdfFonts.Sans, size, style);

    public byte[] Render(QuotationDocument doc)
    {
        // One document at a time: the column geometry depends on the document type.
        lock (_renderLock)
        {
            UseColumns(doc.Kind);
            return RenderCore(doc);
        }
    }

    private byte[] RenderCore(QuotationDocument doc)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{doc.Title} {doc.Number}";
        pdf.Info.Author = doc.Company.CompanyName;
        pdf.Info.Creator = "TS Quotation System";
        pdf.Info.Subject = $"{doc.Title} {doc.Number} — {doc.Buyer.Name}";

        // Measure once with a scratch page so layout is deterministic.
        var measurePage = new PdfDocument().AddPage();
        using var measure = XGraphics.FromPdfPage(measurePage);
        var layout = Layout(measure, doc);

        for (var p = 0; p < layout.Pages.Count; p++)
        {
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(PageWidth);
            page.Height = XUnit.FromPoint(PageHeight);
            using var gfx = XGraphics.FromPdfPage(page);
            DrawPage(gfx, doc, layout, p);
        }

        using var ms = new MemoryStream();
        pdf.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>Page break information, exposed for tests.</summary>
    internal sealed record PageLayout(List<List<RowBlock>> Pages, double HeaderHeight, double FooterHeight, double SummaryHeight);

    internal sealed record RowBlock(PdfLine Line, List<string> NameLines, List<string> DescriptionLines, double Height, bool IsContinuation, bool ShowValues);

    // =====================================================================================
    // Layout
    // =====================================================================================

    private const double TitleHeight = 18;
    private const double TableHeaderHeight = 22;
    private const double TotalRowHeight = 16;
    private const double ContinuedHeight = 14;
    private static double BottomNoteHeight(QuotationDocument doc) =>
        doc.Kind == DocumentType.ProformaInvoice && !string.IsNullOrWhiteSpace(doc.Company.Jurisdiction) ? 22 : 12;

    internal PageLayout Layout(XGraphics g, QuotationDocument doc)
    {
        UseColumns(doc.Kind);
        var headerHeight = HeaderHeight(g, doc);
        var footerHeight = FooterHeight(g, doc);
        var summaryHeight = SummaryHeight(doc);

        var bodyTop = MarginTop + TitleHeight + headerHeight + TableHeaderHeight;
        var pageBottom = PageHeight - MarginBottom - BottomNoteHeight(doc);
        var capacityContinued = pageBottom - ContinuedHeight - bodyTop;
        var capacityLast = pageBottom - footerHeight - TotalRowHeight - summaryHeight - bodyTop;
        if (capacityLast < 0)
        {
            throw new InvalidOperationException(
                "The header and footer do not fit on one page. Shorten the terms & conditions, remarks or addresses.");
        }

        var rows = new List<RowBlock>();
        foreach (var line in doc.Lines) rows.AddRange(RowBlocks(g, line, capacityContinued));

        var pages = new List<List<RowBlock>> { new() };
        var used = 0.0;
        foreach (var row in rows)
        {
            if (used + row.Height > capacityContinued && pages[^1].Count > 0)
            {
                pages.Add([]);
                used = 0;
            }
            pages[^1].Add(row);
            used += row.Height;
        }

        // The last page also needs room for the summary rows, totals and footer: move rows forward if needed.
        while (pages[^1].Sum(r => r.Height) > capacityLast)
        {
            var last = pages[^1];
            var carry = new List<RowBlock>();
            while (last.Count > 0 && last.Sum(r => r.Height) > capacityLast)
            {
                carry.Insert(0, last[^1]);
                last.RemoveAt(last.Count - 1);
            }
            // Keep continuation segments of one item together with the start of that item when possible.
            while (last.Count > 0 && carry.Count > 0 && carry[0].IsContinuation)
            {
                carry.Insert(0, last[^1]);
                last.RemoveAt(last.Count - 1);
            }
            if (last.Count == 0) pages.RemoveAt(pages.Count - 1);
            pages.Add(carry);
            if (carry.Sum(r => r.Height) > capacityLast && carry.Count == 1) break; // cannot improve further
        }
        return new PageLayout(pages, headerHeight, footerHeight, summaryHeight);
    }

    private IEnumerable<RowBlock> RowBlocks(XGraphics g, PdfLine line, double capacity)
    {
        var descWidth = _colW[ColDesc] - 2 * Pad;
        var nameLines = Wrap(g, line.ItemName, _bold, descWidth);
        var descLines = line.DescriptionLines.SelectMany(d => Wrap(g, d, _italic, descWidth - 8)).ToList();
        var height = RowHeight(nameLines.Count, descLines.Count);
        if (height <= capacity)
        {
            yield return new RowBlock(line, nameLines, descLines, height, false, true);
            yield break;
        }

        // A single item taller than a page: split its description lines into continuation segments.
        var perSegment = (int)Math.Floor((capacity - 2 * Pad - nameLines.Count * LineHeight(_bold)) / LineHeight(_italic));
        perSegment = Math.Max(1, perSegment);
        var first = true;
        for (var i = 0; i < descLines.Count; i += perSegment)
        {
            var chunk = descLines.Skip(i).Take(perSegment).ToList();
            var names = first ? nameLines : Wrap(g, line.ItemName + " (contd.)", _bold, descWidth);
            yield return new RowBlock(line, names, chunk, RowHeight(names.Count, chunk.Count), !first, first);
            first = false;
        }
    }

    private double RowHeight(int nameLines, int descLines) =>
        2 * Pad + nameLines * LineHeight(_bold) + descLines * LineHeight(_italic);

    private static double LineHeight(XFont f) => f.Size * 1.22;

    // =====================================================================================
    // Header
    // =====================================================================================

    private double LeftWidth => ContentWidth * 0.5;
    private double RightWidth => ContentWidth - LeftWidth;

    private List<(string Text, XFont Font)> CompanyLines(XGraphics g, QuotationDocument doc, double width)
    {
        var c = doc.Company;
        var lines = new List<(string, XFont)>();
        foreach (var l in Wrap(g, c.CompanyName, _company, width)) lines.Add((l, _company));
        foreach (var a in c.AddressLines)
            foreach (var l in Wrap(g, a, _text, width)) lines.Add((l, _text));
        void Add(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var l in Wrap(g, $"{label}{value}", _text, width)) lines.Add((l, _text));
        }
        Add("Mobile : ", c.Mobile);
        Add("Phone : ", c.Phone);
        Add("GSTIN/UIN: ", c.Gstin);
        if (!string.IsNullOrWhiteSpace(c.StateName) || !string.IsNullOrWhiteSpace(c.StateCode))
            Add("State Name : ", $"{c.StateName}, Code : {c.StateCode}");
        Add("E-Mail : ", c.Email);
        return lines;
    }

    private List<(string Text, XFont Font)> PartyLines(XGraphics g, string caption, PdfParty p, double width)
    {
        var lines = new List<(string, XFont)> { (caption, _label) };
        foreach (var l in Wrap(g, p.Name, _bold, width)) lines.Add((l, _bold));
        foreach (var a in p.AddressLines)
            foreach (var l in Wrap(g, a, _text, width)) lines.Add((l, _text));
        void Add(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var l in Wrap(g, $"{label}{value}", _text, width)) lines.Add((l, _text));
        }
        Add("GSTIN/UIN : ", p.Gstin);
        if (!string.IsNullOrWhiteSpace(p.StateName) || !string.IsNullOrWhiteSpace(p.StateCode))
            Add("State Name : ", $"{p.StateName}{(string.IsNullOrWhiteSpace(p.StateCode) ? "" : $", Code : {p.StateCode}")}");
        Add("Contact : ", string.Join(", ", new[] { p.ContactPerson, p.Phone }.Where(s => !string.IsNullOrWhiteSpace(s))));
        Add("E-Mail : ", p.Email);
        return lines;
    }

    private const double LogoSize = 42;

    private double CompanyTextWidth(QuotationDocument doc) => LeftWidth - 2 * Pad - (doc.Logo is null ? 0 : LogoSize + 6);

    private static double Height(IEnumerable<(string Text, XFont Font)> lines) => lines.Sum(l => LineHeight(l.Font));

    private (double Company, double Consignee, double Buyer) LeftHeights(XGraphics g, QuotationDocument doc)
    {
        var w = LeftWidth - 2 * Pad;
        var company = Math.Max(Height(CompanyLines(g, doc, CompanyTextWidth(doc))), doc.Logo is null ? 0 : LogoSize) + 2 * Pad;
        var consignee = Height(PartyLines(g, "Consignee (Ship to)", doc.Consignee, w)) + 2 * Pad;
        var buyer = Height(PartyLines(g, "Buyer (Bill to)", doc.Buyer, w)) + 2 * Pad;
        return (company, consignee, buyer);
    }

    /// <summary>A cell of the reference grid; <paramref name="JoinAbove"/> merges it with the cell above (no line between).</summary>
    private sealed record Cell(string Label, string Value, bool Full = false, bool JoinAbove = false);

    private static List<Cell> RightCells(QuotationDocument doc)
    {
        var buyersRef = doc.BuyersReference + (doc.BuyersReferenceDate is { } d && doc.BuyersReference.Length > 0 ? $" dt. {Date(d)}" : "");
        if (doc.Kind == DocumentType.ProformaInvoice)
        {
            return
            [
                new("Invoice No.", doc.Number), new("Dated", Date(doc.Date)),
                new("Delivery Note", doc.DeliveryNote), new("Mode/Terms of Payment", doc.PaymentTerms),
                new("Reference No. & Date.", $"{doc.Number} dt. {Date(doc.Date)}"), new("Other References", doc.OtherReferences),
                new("Buyer's Order No.", doc.BuyersReference), new("Dated", doc.BuyersReferenceDate is { } od ? Date(od) : ""),
                new("Dispatch Doc No.", doc.DispatchDocNo), new("Delivery Note Date", doc.DeliveryNoteDate is { } nd ? Date(nd) : ""),
                new("Dispatched through", doc.DispatchedThrough), new("Destination", doc.Destination),
                new("Terms of Delivery", doc.TermsOfDelivery, Full: true),
            ];
        }
        return
        [
            new("Quotation No.", doc.Number), new("Dated", Date(doc.Date)),
            new("", "", JoinAbove: true), new("Mode/Terms of Payment", doc.PaymentTerms),
            new("Buyer's Ref./Order No.", buyersRef), new("Other References", doc.OtherReferences),
            new("Dispatched through", doc.DispatchedThrough), new("Destination", doc.Destination),
            new("Terms of Delivery", doc.TermsOfDelivery, Full: true),
        ];
    }

    private List<(double Height, Cell[] Cells)> RightRows(XGraphics g, QuotationDocument doc)
    {
        var cells = RightCells(doc);
        var rows = new List<(double, Cell[])>();
        var i = 0;
        while (i < cells.Count)
        {
            var row = cells[i].Full ? new[] { cells[i] } : new[] { cells[i], cells[i + 1] };
            i += row.Length;
            var cellWidth = RightWidth / row.Length - 2 * Pad;
            var h = row.Max(c => LineHeight(_label) + Math.Max(1, Wrap(g, c.Value, _bold, cellWidth).Count) * LineHeight(_bold)) + 2 * Pad;
            rows.Add((Math.Max(h, 22), row));
        }
        return rows;
    }

    private double HeaderHeight(XGraphics g, QuotationDocument doc)
    {
        var (company, consignee, buyer) = LeftHeights(g, doc);
        var right = RightRows(g, doc).Sum(r => r.Height);
        return Math.Max(company + consignee + buyer, right);
    }

    private double DrawHeader(XGraphics g, QuotationDocument doc, double top)
    {
        var height = HeaderHeight(g, doc);
        var (companyH, consigneeH, _) = LeftHeights(g, doc);
        var left = MarginLeft;
        var mid = MarginLeft + LeftWidth;
        var pen = Pen();

        // Left column: company, consignee, buyer.
        var y = top + Pad;
        var textX = left + Pad;
        if (doc.Logo is not null)
        {
            try
            {
                using var ms = new MemoryStream(doc.Logo);
                using var img = XImage.FromStream(ms);
                var scale = Math.Min(LogoSize / img.PointWidth, LogoSize / img.PointHeight);
                g.DrawImage(img, left + Pad, y, img.PointWidth * scale, img.PointHeight * scale);
                textX += LogoSize + 6;
            }
            catch (Exception)
            {
                // An unreadable logo must never prevent printing the quotation.
            }
        }
        foreach (var (text, font) in CompanyLines(g, doc, CompanyTextWidth(doc)))
        {
            DrawText(g, text, font, textX, y);
            y += LineHeight(font);
        }

        var consigneeTop = top + companyH;
        g.DrawLine(pen, left, consigneeTop, mid, consigneeTop);
        y = consigneeTop + Pad;
        foreach (var (text, font) in PartyLines(g, "Consignee (Ship to)", doc.Consignee, LeftWidth - 2 * Pad))
        {
            DrawText(g, text, font, left + Pad, y);
            y += LineHeight(font);
        }

        var buyerTop = consigneeTop + consigneeH;
        g.DrawLine(pen, left, buyerTop, mid, buyerTop);
        y = buyerTop + Pad;
        foreach (var (text, font) in PartyLines(g, "Buyer (Bill to)", doc.Buyer, LeftWidth - 2 * Pad))
        {
            DrawText(g, text, font, left + Pad, y);
            y += LineHeight(font);
        }

        // Right column: reference grid; the last row (Terms of Delivery) stretches to the bottom.
        g.DrawLine(pen, mid, top, mid, top + height);
        var rows = RightRows(g, doc);
        y = top;
        for (var r = 0; r < rows.Count; r++)
        {
            var (rowHeight, cells) = rows[r];
            var h = r == rows.Count - 1 ? top + height - y : rowHeight;
            var cellWidth = RightWidth / cells.Length;
            for (var c = 0; c < cells.Length; c++)
            {
                var x = mid + c * cellWidth;
                if (c > 0) g.DrawLine(pen, x, y, x, y + h);
                DrawText(g, cells[c].Label, _label, x + Pad, y + Pad);
                var vy = y + Pad + LineHeight(_label);
                foreach (var l in Wrap(g, cells[c].Value, _bold, cellWidth - 2 * Pad))
                {
                    DrawText(g, l, _bold, x + Pad, vy);
                    vy += LineHeight(_bold);
                }
            }
            if (r < rows.Count - 1)
            {
                // No line above a cell that is merged with the one above it.
                var next = rows[r + 1].Cells;
                var nextWidth = RightWidth / next.Length;
                for (var c = 0; c < next.Length; c++)
                {
                    if (next[c].JoinAbove) continue;
                    g.DrawLine(pen, mid + c * nextWidth, y + h, mid + (c + 1) * nextWidth, y + h);
                }
            }
            y += h;
        }
        g.DrawLine(pen, MarginLeft, top + height, MarginLeft + ContentWidth, top + height);
        return top + height;
    }

    // =====================================================================================
    // Table
    // =====================================================================================

    private double DrawTableHeader(XGraphics g, double top)
    {
        var pen = Pen();
        for (var i = 0; i < Columns.Length; i++)
        {
            var lines = Columns[i].Title.Split('\n');
            var textHeight = lines.Length * LineHeight(_head);
            var y = top + (TableHeaderHeight - textHeight) / 2;
            foreach (var l in lines)
            {
                DrawAligned(g, l, _head, _colX[i], _colW[i], y, Align.Center);
                y += LineHeight(_head);
            }
        }
        g.DrawLine(pen, MarginLeft, top + TableHeaderHeight, MarginLeft + ContentWidth, top + TableHeaderHeight);
        return top + TableHeaderHeight;
    }

    private double DrawRow(XGraphics g, RowBlock row, double top)
    {
        var line = row.Line;
        var y = top + Pad;
        if (row.ShowValues)
        {
            DrawAligned(g, line.SlNo.ToString(CultureInfo.InvariantCulture), _text, _colX[ColSl], _colW[ColSl], y, Align.Center);
            DrawCell(g, line.Hsn, _text, ColHsn, y);
            DrawCell(g, line.GstRate is null ? "" : $"{line.GstRate.Value.ToString("0.##", CultureInfo.InvariantCulture)} %", _text, ColGst, y);
            if (Columns == ProformaColumns) DrawCell(g, line.PartNumber, _text, ColRef, y);
            else DrawCell(g, line.DueOn, Font(7, XFontStyleEx.Italic), ColRef, y);
            DrawCell(g, $"{IndianFormat.Quantity(line.Quantity)} {line.Unit}".Trim(), _bold, ColQty, y);
            DrawCell(g, IndianFormat.Amount(line.Rate), _text, ColRate, y);
            DrawCell(g, line.Unit, Font(6.5, XFontStyleEx.Regular), ColPer, y + 0.8);
            DrawCell(g, line.DiscountPercent == 0 ? "" : $"{line.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture)} %", _text, ColDisc, y);
            DrawCell(g, IndianFormat.Amount(line.Amount), _bold, ColAmount, y);
        }
        foreach (var n in row.NameLines)
        {
            DrawText(g, n, _bold, _colX[ColDesc] + Pad, y);
            y += LineHeight(_bold);
        }
        foreach (var d in row.DescriptionLines)
        {
            DrawText(g, d, _italic, _colX[ColDesc] + Pad + 8, y);
            y += LineHeight(_italic);
        }
        return top + row.Height;
    }

    private double SummaryHeight(QuotationDocument doc) =>
        doc.SummaryRows.Count == 0 ? 0 : 2 + LineHeight(_text) + doc.SummaryRows.Count * (LineHeight(_boldItalic) + 1) + Pad;

    private double DrawSummary(XGraphics g, QuotationDocument doc, double top)
    {
        if (doc.SummaryRows.Count == 0) return top;
        var y = top + 2;
        var amountX = _colX[ColAmount];
        g.DrawLine(Pen(0.5), amountX + 8, y, amountX + _colW[ColAmount] - Pad, y);
        DrawCell(g, IndianFormat.Amount(doc.Subtotal), _text, ColAmount, y + 1);
        y += LineHeight(_text);
        foreach (var s in doc.SummaryRows)
        {
            if (s.Less) DrawText(g, "Less :", Font(7, XFontStyleEx.Italic), _colX[ColDesc] + Pad, y + 1);
            DrawAligned(g, s.Label, _boldItalic, _colX[ColDesc] + Pad, _colW[ColDesc] - 2 * Pad, y, Align.Right);
            DrawCell(g, s.RateText, _text, ColRate, y);
            DrawCell(g, s.Less ? "(-)" + IndianFormat.Amount(Math.Abs(s.Amount)) : IndianFormat.Amount(s.Amount), _bold, ColAmount, y);
            y += LineHeight(_boldItalic) + 1;
        }
        return top + SummaryHeight(doc);
    }

    private void DrawColumnLines(XGraphics g, double top, double bottom)
    {
        var pen = Pen();
        for (var i = 1; i < _colX.Length; i++) g.DrawLine(pen, _colX[i], top, _colX[i], bottom);
    }

    private double DrawTotalRow(XGraphics g, QuotationDocument doc, double top)
    {
        var pen = Pen();
        g.DrawLine(pen, MarginLeft, top, MarginLeft + ContentWidth, top);
        var y = top + (TotalRowHeight - LineHeight(_bold)) / 2;
        DrawAligned(g, "Total", _bold, _colX[ColDesc] + Pad, _colW[ColDesc] - 2 * Pad, y, Align.Right);
        var qty = doc.TotalQuantityUnit.Length > 0
            ? $"{IndianFormat.Quantity(doc.TotalQuantity)} {doc.TotalQuantityUnit}"
            : "";
        DrawCell(g, qty, _bold, ColQty, y);
        DrawCell(g, "₹ " + IndianFormat.Amount(doc.GrandTotal), _bold, ColAmount, y);
        g.DrawLine(pen, MarginLeft, top + TotalRowHeight, MarginLeft + ContentWidth, top + TotalRowHeight);
        return top + TotalRowHeight;
    }

    private void DrawCell(XGraphics g, string text, XFont font, int col, double y) =>
        DrawAligned(g, Fit(g, text, font, _colW[col] - 2 * Pad), font, _colX[col] + Pad, _colW[col] - 2 * Pad, y, Columns[col].Align);

    // =====================================================================================
    // Footer (last page)
    // =====================================================================================

    private sealed record FooterParts(
        List<string> Words,
        List<string> Remarks,
        List<string> Terms,
        List<(string Text, XFont Font)> LeftBlock,
        List<(string Text, XFont Font)> RightBlock,
        List<string> TaxWords);

    private bool IsProforma(QuotationDocument doc) => doc.Kind == DocumentType.ProformaInvoice;

    private FooterParts BuildFooter(XGraphics g, QuotationDocument doc)
    {
        var fullWidth = ContentWidth - 2 * Pad;
        var words = Wrap(g, doc.AmountInWords, _words, fullWidth);
        var remarks = string.IsNullOrWhiteSpace(doc.Remarks) ? [] : Wrap(g, "Remarks: " + doc.Remarks, _text, fullWidth);
        var terms = new List<string>();
        for (var i = 0; i < doc.TermsAndConditions.Count; i++)
        {
            terms.AddRange(Wrap(g, $"{i + 1}. {doc.TermsAndConditions[i]}", _small, fullWidth - 6));
        }

        var c = doc.Company;
        var half = ContentWidth / 2 - 2 * Pad;
        var left = new List<(string, XFont)>();
        void AddLeft(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) left.AddRange(Wrap(g, label + value, _text, half).Select(l => (l, _text)));
        }
        if (!IsProforma(doc)) AddLeft("Company's PAN : ", c.Pan);
        AddLeft("Company's MSME No : ", c.MsmeNumber);
        AddLeft("Company's IEC : ", c.Iec);
        var declaration = IsProforma(doc) ? c.ProformaDeclaration : c.Declaration;
        if (!string.IsNullOrWhiteSpace(declaration))
        {
            left.Add(("Declaration", _label));
            left.AddRange(Wrap(g, declaration, _text, half).Select(l => (l, _text)));
        }
        if (!IsProforma(doc))
        {
            var validity = c.ValidityText.Replace("{days}", c.QuotationValidityDays.ToString(CultureInfo.InvariantCulture))
                .Replace("{date}", Date(doc.Date.AddDays(c.QuotationValidityDays)));
            if (c.QuotationValidityDays > 0 && !string.IsNullOrWhiteSpace(validity))
            {
                left.AddRange(Wrap(g, validity, _small, half).Select(l => (l, _small)));
            }
        }

        var right = new List<(string, XFont)>();
        if (!string.IsNullOrWhiteSpace(c.BankName) || !string.IsNullOrWhiteSpace(c.BankAccountNumber))
        {
            right.Add(("Company's Bank Details", _text));
            void AddRight(string label, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) right.AddRange(Wrap(g, label + value, _text, half).Select(l => (l, _text)));
            }
            AddRight("A/c Holder's Name : ", c.BankAccountHolder);
            AddRight("Bank Name : ", c.BankName);
            AddRight("A/c No. : ", c.BankAccountNumber);
            var branch = string.Join(" & ", new[] { c.BankBranch, c.BankIfsc }.Where(x => !string.IsNullOrWhiteSpace(x)));
            AddRight("Branch & IFS Code : ", branch);
        }
        var taxWords = IsProforma(doc) && doc.HsnSummary.Count > 0 && !string.IsNullOrWhiteSpace(doc.TaxAmountInWords)
            ? Wrap(g, "Tax Amount (in words) :  " + doc.TaxAmountInWords, _bold, fullWidth)
            : [];
        return new FooterParts(words, remarks, terms, left, right, taxWords);
    }

    private const double SignatureHeight = 46;

    // ---- HSN/SAC-wise tax table (proforma invoice) ----
    private const double HsnTaxableW = 62, HsnRateW = 26, HsnAmountW = 52, HsnTotalW = 58;

    private double HsnTableHeight(QuotationDocument doc) =>
        !IsProforma(doc) || doc.HsnSummary.Count == 0 ? 0 : 2 * LineHeight(_label) + 2 + (doc.HsnSummary.Count + 1) * (LineHeight(_text) + 2);

    private double DrawHsnTable(XGraphics g, QuotationDocument doc, double top)
    {
        if (HsnTableHeight(doc) == 0) return top;
        var pen = Pen(0.5);
        var right = MarginLeft + ContentWidth;
        var groups = doc.InterState ? new[] { "IGST" } : new[] { "CGST", "SGST/UTGST" };
        var groupW = HsnRateW + HsnAmountW;
        var taxableX = right - HsnTotalW - groups.Length * groupW - HsnTaxableW;
        var headH = 2 * LineHeight(_label) + 2;
        var rowH = LineHeight(_text) + 2;

        // Heading
        g.DrawLine(pen, MarginLeft, top, right, top);
        DrawAligned(g, "HSN/SAC", _label, MarginLeft, taxableX - MarginLeft, top + headH / 2 - LineHeight(_label) / 2, Align.Center);
        DrawAligned(g, "Taxable", _label, taxableX, HsnTaxableW, top + 1, Align.Center);
        DrawAligned(g, "Value", _label, taxableX, HsnTaxableW, top + 1 + LineHeight(_label), Align.Center);
        var x = taxableX + HsnTaxableW;
        foreach (var name in groups)
        {
            DrawAligned(g, name, _label, x, groupW, top + 1, Align.Center);
            g.DrawLine(pen, x, top + LineHeight(_label) + 1, x + groupW, top + LineHeight(_label) + 1);
            DrawAligned(g, "Rate", _label, x, HsnRateW, top + 1 + LineHeight(_label), Align.Center);
            DrawAligned(g, "Amount", _label, x + HsnRateW, HsnAmountW, top + 1 + LineHeight(_label), Align.Center);
            g.DrawLine(pen, x + HsnRateW, top + LineHeight(_label) + 1, x + HsnRateW, top + headH + (doc.HsnSummary.Count + 1) * rowH);
            x += groupW;
        }
        DrawAligned(g, "Total", _label, x, HsnTotalW, top + 1, Align.Center);
        DrawAligned(g, "Tax Amount", _label, x, HsnTotalW, top + 1 + LineHeight(_label), Align.Center);
        var bottom = top + headH + (doc.HsnSummary.Count + 1) * rowH;
        foreach (var vx in new[] { taxableX, taxableX + HsnTaxableW }.Concat(Enumerable.Range(1, groups.Length).Select(i => taxableX + HsnTaxableW + i * groupW)))
        {
            g.DrawLine(pen, vx, top, vx, bottom);
        }
        var y = top + headH;
        g.DrawLine(pen, MarginLeft, y, right, y);

        string Pct(decimal r) => r == 0 ? "" : r.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        void Row(string hsn, decimal taxable, (decimal Rate, decimal Amount)[] parts, decimal total, bool bold)
        {
            var f = bold ? _bold : _text;
            if (bold) DrawAligned(g, hsn, f, MarginLeft + Pad, taxableX - MarginLeft - 2 * Pad, y + 1, Align.Right);
            else DrawText(g, hsn, f, MarginLeft + Pad, y + 1);
            DrawAligned(g, IndianFormat.Amount(taxable), f, taxableX, HsnTaxableW - Pad, y + 1, Align.Right);
            var px = taxableX + HsnTaxableW;
            foreach (var (rate, amount) in parts)
            {
                if (!bold) DrawAligned(g, Pct(rate), f, px, HsnRateW - 2, y + 1, Align.Right);
                DrawAligned(g, IndianFormat.Amount(amount), f, px + HsnRateW, HsnAmountW - Pad, y + 1, Align.Right);
                px += groupW;
            }
            DrawAligned(g, IndianFormat.Amount(total), f, px, HsnTotalW - Pad, y + 1, Align.Right);
            y += rowH;
        }
        foreach (var h in doc.HsnSummary)
        {
            var parts = doc.InterState
                ? new[] { (h.IntegratedRate, h.IntegratedAmount) }
                : new[] { (h.CentralRate, h.CentralAmount), (h.StateRate, h.StateAmount) };
            Row(h.Hsn, h.TaxableValue, parts, h.TotalTax, bold: false);
        }
        g.DrawLine(pen, taxableX, y, right, y);
        var totals = doc.InterState
            ? new[] { (0m, doc.HsnSummary.Sum(h => h.IntegratedAmount)) }
            : new[] { (0m, doc.HsnSummary.Sum(h => h.CentralAmount)), (0m, doc.HsnSummary.Sum(h => h.StateAmount)) };
        Row("Total", doc.HsnSummary.Sum(h => h.TaxableValue), totals, doc.HsnSummary.Sum(h => h.TotalTax), bold: true);
        g.DrawLine(pen, MarginLeft, bottom, right, bottom);
        return bottom;
    }

    private double FooterHeight(XGraphics g, QuotationDocument doc)
    {
        var f = BuildFooter(g, doc);
        var h = Pad + LineHeight(_label) + f.Words.Count * LineHeight(_words) + Pad;
        h += HsnTableHeight(doc);
        if (f.TaxWords.Count > 0) h += f.TaxWords.Count * LineHeight(_bold) + 2;
        if (f.Remarks.Count > 0) h += f.Remarks.Count * LineHeight(_text) + 2;
        if (f.Terms.Count > 0) h += LineHeight(_bold) + f.Terms.Count * LineHeight(_small) + 2;
        h += Math.Max(Height(f.LeftBlock), Height(f.RightBlock)) + 2 * Pad;
        h += SignatureHeight;
        return h;
    }

    private double DrawFooter(XGraphics g, QuotationDocument doc, double top)
    {
        var f = BuildFooter(g, doc);
        var pen = Pen();
        var y = top + Pad;
        DrawText(g, "Amount Chargeable (in words)", _label, MarginLeft + Pad, y);
        DrawAligned(g, "E. & O.E", Font(7, XFontStyleEx.Italic), MarginLeft, ContentWidth - Pad, y, Align.Right);
        y += LineHeight(_label);
        foreach (var w in f.Words)
        {
            DrawText(g, w, _words, MarginLeft + Pad, y);
            y += LineHeight(_words);
        }
        y += Pad;
        y = DrawHsnTable(g, doc, y);
        if (f.TaxWords.Count > 0)
        {
            y += 1;
            foreach (var w in f.TaxWords)
            {
                DrawText(g, w, _bold, MarginLeft + Pad, y);
                y += LineHeight(_bold);
            }
            y += 1;
        }
        foreach (var r in f.Remarks)
        {
            DrawText(g, r, _text, MarginLeft + Pad, y);
            y += LineHeight(_text);
        }
        if (f.Remarks.Count > 0) y += 2;
        if (f.Terms.Count > 0)
        {
            DrawText(g, "Terms & Conditions", _bold, MarginLeft + Pad, y);
            y += LineHeight(_bold);
            foreach (var t in f.Terms)
            {
                DrawText(g, t, _small, MarginLeft + Pad + 6, y);
                y += LineHeight(_small);
            }
            y += 2;
        }

        var half = ContentWidth / 2;
        var blockTop = y + Pad;
        var ly = blockTop;
        foreach (var (text, font) in f.LeftBlock)
        {
            DrawText(g, text, font, MarginLeft + Pad, ly);
            ly += LineHeight(font);
        }
        var ry = blockTop;
        foreach (var (text, font) in f.RightBlock)
        {
            DrawText(g, text, font, MarginLeft + half + Pad, ry);
            ry += LineHeight(font);
        }
        y = Math.Max(ly, ry) + Pad;

        // Signature block: "for COMPANY … Authorised Signatory" box on the right; on the left "Prepared by / Verified by"
        // (quotation) or "Customer's Seal and Signature" (proforma invoice).
        g.DrawLine(pen, MarginLeft + half, y, MarginLeft + ContentWidth, y);
        g.DrawLine(pen, MarginLeft + half, y, MarginLeft + half, y + SignatureHeight);
        DrawAligned(g, "for " + doc.Company.CompanyName, _bold, MarginLeft + half, half - Pad, y + Pad, Align.Right);
        DrawAligned(g, doc.Company.AuthorisedSignatoryLabel, _text, MarginLeft + half, half - Pad, y + SignatureHeight - Pad - LineHeight(_text), Align.Right);
        var sigBottom = y + SignatureHeight - Pad - LineHeight(_text);
        if (IsProforma(doc))
        {
            g.DrawLine(pen, MarginLeft, y, MarginLeft + half, y);
            DrawText(g, "Customer's Seal and Signature", _text, MarginLeft + Pad, y + Pad);
        }
        else
        {
            DrawText(g, "Prepared by", _text, MarginLeft + Pad, sigBottom);
            DrawText(g, "Verified by", _text, MarginLeft + half / 2, sigBottom);
            if (!string.IsNullOrWhiteSpace(doc.PreparedBy)) DrawText(g, doc.PreparedBy, _bold, MarginLeft + Pad, sigBottom - LineHeight(_bold));
            if (!string.IsNullOrWhiteSpace(doc.VerifiedBy)) DrawText(g, doc.VerifiedBy, _bold, MarginLeft + half / 2, sigBottom - LineHeight(_bold));
        }
        return y + SignatureHeight;
    }

    // =====================================================================================
    // Page assembly
    // =====================================================================================

    private void DrawPage(XGraphics g, QuotationDocument doc, PageLayout layout, int pageIndex)
    {
        var isLast = pageIndex == layout.Pages.Count - 1;
        var pageCount = layout.Pages.Count;

        if (!string.IsNullOrWhiteSpace(doc.Watermark)) DrawWatermark(g, doc.Watermark!);

        // "Printed on … By: …" above the title of the first page, as in Tally.
        if (pageIndex == 0 && doc.Printed is { } printed)
        {
            var by = "By: " + printed.User;
            var byFont = Font(8, XFontStyleEx.Regular);
            var stampY = MarginTop - 12;
            DrawAligned(g, by, byFont, MarginLeft, ContentWidth, stampY, Align.Right);
            var at = $"Printed on {printed.LocalTime.ToString("d-MMM-yy", CultureInfo.InvariantCulture)} at {printed.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)}";
            DrawAligned(g, at, Font(7.5, XFontStyleEx.Italic), MarginLeft, ContentWidth - Measure(g, by, byFont) - 36, stampY + 0.5, Align.Right);
        }

        // Title line: "QUOTATION", then "QUOTATION(Page 2)" … on following pages.
        var title = pageIndex == 0 || pageCount == 1 ? doc.Title : $"{doc.Title}(Page {pageIndex + 1})";
        DrawAligned(g, title, _title, MarginLeft, ContentWidth, MarginTop + 2, Align.Center);

        var boxTop = MarginTop + TitleHeight;
        var y = DrawHeader(g, doc, boxTop);
        var tableTop = y;
        y = DrawTableHeader(g, y);
        foreach (var row in layout.Pages[pageIndex]) y = DrawRow(g, row, y);

        var pageBottom = PageHeight - MarginBottom - BottomNoteHeight(doc);
        double boxBottom;
        if (isLast)
        {
            var footerTop = pageBottom - layout.FooterHeight;
            var totalTop = footerTop - TotalRowHeight;
            DrawSummary(g, doc, totalTop - layout.SummaryHeight);
            DrawColumnLines(g, tableTop, totalTop + TotalRowHeight);
            DrawTotalRow(g, doc, totalTop);
            boxBottom = DrawFooter(g, doc, footerTop);
            var noteY = boxBottom + 3;
            if (IsProforma(doc) && !string.IsNullOrWhiteSpace(doc.Company.Jurisdiction))
            {
                DrawAligned(g, doc.Company.Jurisdiction, _small, MarginLeft, ContentWidth, noteY, Align.Center);
                noteY += 10;
            }
            DrawAligned(g, IsProforma(doc) ? doc.Company.ProformaFooterText : doc.Company.FooterText, _small, MarginLeft, ContentWidth, noteY, Align.Center);
        }
        else
        {
            var tableBottom = pageBottom - ContinuedHeight;
            DrawColumnLines(g, tableTop, tableBottom);
            g.DrawLine(Pen(), MarginLeft, tableBottom, MarginLeft + ContentWidth, tableBottom);
            DrawAligned(g, "continued ...", Font(7, XFontStyleEx.Italic), MarginLeft, ContentWidth - Pad, tableBottom + 3, Align.Right);
            boxBottom = pageBottom;
        }
        g.DrawRectangle(Pen(), MarginLeft, boxTop, ContentWidth, boxBottom - boxTop);
    }

    private void DrawWatermark(XGraphics g, string text)
    {
        var state = g.Save();
        g.TranslateTransform(PageWidth / 2, PageHeight / 2);
        g.RotateTransform(-35);
        var font = Font(90, XFontStyleEx.Bold);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, new XSolidBrush(XColor.FromArgb(28, 200, 0, 0)), -size.Width / 2, size.Height / 3);
        g.Restore(state);
    }

    // =====================================================================================
    // Text helpers
    // =====================================================================================

    private static XPen Pen(double width = 0.6) => new(XColors.Black, width);

    private static string Date(DateOnly d) => d.ToString("d-MMM-yy", CultureInfo.InvariantCulture);

    /// <summary>Draws text with its top at <paramref name="y"/>; ₹ is drawn with the symbol font.</summary>
    private static void DrawText(XGraphics g, string text, XFont font, double x, double y)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!text.Contains('₹'))
        {
            g.DrawString(text, font, XBrushes.Black, x, y, XStringFormats.TopLeft);
            return;
        }
        var symbol = new XFont(PdfFonts.Symbols, font.Size, font.Style);
        foreach (var part in SplitRupee(text))
        {
            var f = part == "₹" ? symbol : font;
            g.DrawString(part, f, XBrushes.Black, x, y + (part == "₹" ? -0.3 : 0), XStringFormats.TopLeft);
            x += g.MeasureString(part, f).Width;
        }
    }

    private static double Measure(XGraphics g, string text, XFont font)
    {
        if (!text.Contains('₹')) return g.MeasureString(text, font).Width;
        var symbol = new XFont(PdfFonts.Symbols, font.Size, font.Style);
        return SplitRupee(text).Sum(p => g.MeasureString(p, p == "₹" ? symbol : font).Width);
    }

    private static IEnumerable<string> SplitRupee(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '₹') continue;
            if (i > start) yield return text[start..i];
            yield return "₹";
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    private static void DrawAligned(XGraphics g, string text, XFont font, double x, double width, double y, Align align)
    {
        if (string.IsNullOrEmpty(text)) return;
        var w = Measure(g, text, font);
        var dx = align switch
        {
            Align.Right => width - w,
            Align.Center => (width - w) / 2,
            _ => 0,
        };
        DrawText(g, text, font, x + Math.Max(0, dx), y);
    }

    /// <summary>Shrinks nothing: if a single-line value is too wide it is shortened with an ellipsis (numeric columns only).</summary>
    private static string Fit(XGraphics g, string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text) || Measure(g, text, font) <= width) return text;
        var t = text;
        while (t.Length > 1 && Measure(g, t + "…", font) > width) t = t[..^1];
        return t + "…";
    }

    /// <summary>Word-wraps text (honouring line breaks); words longer than the width are broken.</summary>
    internal static List<string> Wrap(XGraphics g, string? text, XFont font, double width)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            var line = "";
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(g, candidate, font) <= width)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0) result.Add(line);
                // Break words that alone exceed the width.
                var w = word;
                while (Measure(g, w, font) > width && w.Length > 1)
                {
                    var n = w.Length - 1;
                    while (n > 1 && Measure(g, w[..n], font) > width) n--;
                    result.Add(w[..n]);
                    w = w[n..];
                }
                line = w;
            }
            if (line.Length > 0) result.Add(line);
        }
        return result;
    }
}
