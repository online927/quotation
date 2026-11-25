using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Quotation.Pdf;

/// <summary>
/// Round 38 (PL-1): the "View product" line under an item that has a product web page — blue, underlined and clickable
/// in the PDF (a web-link annotation over the words).
/// </summary>
internal static class PdfLinks
{
    /// <summary>Marks a description line as the item's link line (the text after the mark is printed).</summary>
    public const char Mark = '\u0002';
    public const string Text = "View product";

    public static readonly XColor Blue = XColor.FromArgb(0x1A, 0x5F, 0xD0);

    public static string LineFor(PdfLine line) => Mark + Text;

    public static bool IsLinkLine(string text) => text.Length > 0 && text[0] == Mark;

    /// <summary>
    /// Draws the words at (<paramref name="x"/>, <paramref name="top"/>) — top of the text — in blue, underlined, and makes
    /// them open <paramref name="url"/>.
    /// </summary>
    public static void Draw(XGraphics g, XFont font, double x, double top, string url)
    {
        var size = g.MeasureString(Text, font);
        g.DrawString(Text, font, new XSolidBrush(Blue), x, top, XStringFormats.TopLeft);
        var underline = top + size.Height - 1.2;
        g.DrawLine(new XPen(Blue, 0.4), x, underline, x + size.Width, underline);
        if (g.PdfPage is PdfPage page && url.Length > 0)
        {
            var rect = g.Transformer.WorldToDefaultPage(new XRect(x, top, size.Width, size.Height));
            page.AddWebLink(new PdfRectangle(rect), url);
        }
    }
}
