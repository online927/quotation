using System.Reflection;
using PdfSharp.Fonts;

namespace Quotation.Pdf;

/// <summary>
/// Bundled fonts so every PC renders identical PDFs: Liberation Sans (metrically compatible
/// with Arial, the font of Tally's print) and DejaVu Sans for the ₹ symbol. Both are freely
/// redistributable (SIL OFL / Bitstream Vera licences, see Fonts/).
/// </summary>
internal sealed class PdfFonts : IFontResolver
{
    public const string Sans = "Liberation Sans";
    public const string Symbols = "DejaVu Sans";

    private static readonly Lock Gate = new();
    private static bool _installed;

    public static void EnsureInstalled()
    {
        lock (Gate)
        {
            if (_installed) return;
            if (GlobalFontSettings.FontResolver is not PdfFonts)
            {
                GlobalFontSettings.FontResolver = new PdfFonts();
            }
            _installed = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName.Equals(Symbols, StringComparison.OrdinalIgnoreCase))
        {
            return new FontResolverInfo("DejaVuSans", bold, italic);
        }
        var face = (bold, italic) switch
        {
            (true, true) => "LiberationSans-BoldItalic",
            (true, false) => "LiberationSans-Bold",
            (false, true) => "LiberationSans-Italic",
            _ => "LiberationSans-Regular",
        };
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        var name = $"Quotation.Pdf.Fonts.{faceName}.ttf";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Font resource {name} is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
