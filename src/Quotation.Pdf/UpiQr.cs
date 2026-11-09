using System.Globalization;
using PdfSharp.Drawing;
using QRCoder;
using Quotation.Core.Domain;

namespace Quotation.Pdf;

/// <summary>
/// "Pay by UPI" QR code on quotations (not proforma invoices), switched on in Settings → Company with the company's UPI ID.
/// Scanning it opens the customer's UPI app (Google Pay, PhonePe, Paytm, BHIM …) with the UPI ID, the quotation total and the
/// quotation number filled in. Drawn as squares (sharp at any zoom and when printed); nothing is sent anywhere.
/// </summary>
public static class UpiQr
{
    /// <summary>The upi:// link for this document, or null when there is no QR on it.</summary>
    public static string? LinkFor(QuotationDocument doc)
    {
        var c = doc.Company;
        var upi = (c.UpiId ?? "").Trim();
        if (doc.Kind != DocumentType.Quotation || !c.UpiQrOnQuotations || upi.Length == 0) return null;
        static string E(string s) => Uri.EscapeDataString(s);
        var name = string.IsNullOrWhiteSpace(c.CompanyName) ? upi : c.CompanyName.Trim();
        // The UPI ID as typed (letters, digits, . _ - and one @; checked in Settings): some UPI apps do not accept "%40".
        var plain = upi.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or '@');
        var link = $"upi://pay?pa={(plain ? upi : E(upi))}&pn={E(name)}&cu=INR";
        if (doc.GrandTotal > 0) link += "&am=" + doc.GrandTotal.ToString("0.00", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(doc.Number)) link += "&tn=" + E(doc.Number);
        return link;
    }

    /// <summary>Draws the QR code as a square of <paramref name="size"/> points with its top-left corner at (x, y).</summary>
    public static void Draw(XGraphics g, string link, double x, double y, double size)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        const int quiet = 4; // QRCoder adds a 4-module white border; the cell around the code is white already
        var modules = matrix.Count - 2 * quiet;
        var m = size / modules;
        for (var row = 0; row < modules; row++)
        {
            var bits = matrix[row + quiet];
            var col = 0;
            while (col < modules)
            {
                if (!bits[col + quiet]) { col++; continue; }
                var start = col;
                while (col < modules && bits[col + quiet]) col++;
                // One rectangle per run of dark modules, slightly overlapping to avoid hairline gaps in viewers.
                g.DrawRectangle(XBrushes.Black, x + start * m, y + row * m, (col - start) * m + 0.02, m + 0.02);
            }
        }
    }
}
