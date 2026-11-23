using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Quotation.Pdf;

/// <summary>Round 36: joins PDFs (a quotation, then its items' datasheets) and counts pages of an uploaded PDF.</summary>
public static class PdfAppender
{
    /// <summary>The pages of an uploaded PDF, or null when it is not a PDF that can be read (damaged, password-protected).</summary>
    public static int? PageCount(byte[] pdf)
    {
        try
        {
            using var stream = new MemoryStream(pdf);
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="main"/> followed by every page of each extra PDF. An extra that cannot be read is left out, so a bad
    /// datasheet never stops a quotation PDF.
    /// </summary>
    public static byte[] Append(byte[] main, IEnumerable<byte[]> extras)
    {
        var list = extras.ToList();
        if (list.Count == 0) return main;
        using var mainStream = new MemoryStream(main);
        using var output = PdfReader.Open(mainStream, PdfDocumentOpenMode.Modify);
        var added = 0;
        foreach (var extra in list)
        {
            try
            {
                using var s = new MemoryStream(extra);
                using var doc = PdfReader.Open(s, PdfDocumentOpenMode.Import);
                foreach (var page in doc.Pages) output.AddPage(page);
                added++;
            }
            catch (Exception)
            {
                // A datasheet that cannot be read is skipped.
            }
        }
        if (added == 0) return main;
        using var result = new MemoryStream();
        output.Save(result, false);
        return result.ToArray();
    }
}
