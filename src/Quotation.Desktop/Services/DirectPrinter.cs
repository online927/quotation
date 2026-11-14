using System.Runtime.InteropServices;
using System.Text;

namespace Quotation.Desktop.Services;

/// <summary>
/// Prints a PDF straight to the Windows default printer, with no print window and no PDF viewer: PDFium draws each page on
/// the printer (sharp, as the PDF itself), scaled to fit the paper. Windows only.
/// </summary>
public static partial class DirectPrinter
{
    private static readonly object Gate = new();
    private static bool _pdfiumReady;

    /// <summary>Prints <paramref name="pdf"/> on the default printer; returns the printer's name.</summary>
    public static string Print(byte[] pdf, string documentName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Printing straight to the printer works on Windows.");
        var printer = DefaultPrinter() ?? throw new InvalidOperationException("No default printer is set in Windows (Settings → Bluetooth & devices → Printers).");
        lock (Gate)
        {
            if (!_pdfiumReady)
            {
                FPDF_InitLibrary();
                _pdfiumReady = true;
            }
            var data = Marshal.AllocHGlobal(pdf.Length);
            var doc = IntPtr.Zero;
            var dc = IntPtr.Zero;
            try
            {
                Marshal.Copy(pdf, 0, data, pdf.Length);
                doc = FPDF_LoadMemDocument(data, pdf.Length, null);
                if (doc == IntPtr.Zero) throw new InvalidOperationException("The PDF could not be read for printing.");
                dc = CreateDC("WINSPOOL", printer, null, IntPtr.Zero);
                if (dc == IntPtr.Zero) throw new InvalidOperationException($"The printer \"{printer}\" could not be opened.");
                var info = new DocInfo { Size = Marshal.SizeOf<DocInfo>(), DocName = documentName };
                if (StartDoc(dc, ref info) <= 0) throw new InvalidOperationException($"The printer \"{printer}\" did not take the document (is it switched on?).");
                var pages = FPDF_GetPageCount(doc);
                int dpiX = GetDeviceCaps(dc, LogPixelsX), dpiY = GetDeviceCaps(dc, LogPixelsY);
                int paperW = GetDeviceCaps(dc, PhysicalWidth), paperH = GetDeviceCaps(dc, PhysicalHeight);
                int offX = GetDeviceCaps(dc, PhysicalOffsetX), offY = GetDeviceCaps(dc, PhysicalOffsetY);
                for (var i = 0; i < pages; i++)
                {
                    var page = FPDF_LoadPage(doc, i);
                    if (page == IntPtr.Zero) continue;
                    try
                    {
                        // The page at its real size, made smaller only if it does not fit the paper; placed from the paper's edge.
                        var w = FPDF_GetPageWidthF(page) / 72.0 * dpiX;
                        var h = FPDF_GetPageHeightF(page) / 72.0 * dpiY;
                        var scale = Math.Min(1.0, Math.Min(paperW / w, paperH / h));
                        var width = (int)Math.Round(w * scale);
                        var height = (int)Math.Round(h * scale);
                        var x = (paperW - width) / 2 - offX;
                        var y = (paperH - height) / 2 - offY;
                        StartPage(dc);
                        FPDF_RenderPage(dc, page, x, y, width, height, 0, FpdfAnnot | FpdfPrinting);
                        EndPage(dc);
                    }
                    finally
                    {
                        FPDF_ClosePage(page);
                    }
                }
                EndDoc(dc);
                return printer;
            }
            finally
            {
                if (dc != IntPtr.Zero) DeleteDC(dc);
                if (doc != IntPtr.Zero) FPDF_CloseDocument(doc);
                Marshal.FreeHGlobal(data);
            }
        }
    }

    private static string? DefaultPrinter()
    {
        var size = 0;
        GetDefaultPrinter(null, ref size);
        if (size <= 0) return null;
        var name = new StringBuilder(size);
        return GetDefaultPrinter(name, ref size) ? name.ToString() : null;
    }

    private const int LogPixelsX = 88, LogPixelsY = 90, PhysicalWidth = 110, PhysicalHeight = 111, PhysicalOffsetX = 112, PhysicalOffsetY = 113;
    private const int FpdfAnnot = 0x01, FpdfPrinting = 0x800;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        public int Size;
        public string DocName;
        public string? Output;
        public string? DataType;
        public int Type;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? buffer, ref int size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDCW")]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr devMode);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartDocW")] private static extern int StartDoc(IntPtr dc, ref DocInfo info);
    [DllImport("gdi32.dll")] private static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int EndPage(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);

    [DllImport("pdfium")] private static extern void FPDF_InitLibrary();
    [DllImport("pdfium", CharSet = CharSet.Ansi)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string? password);
    [DllImport("pdfium")] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport("pdfium")] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport("pdfium")] private static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport("pdfium")] private static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport("pdfium")] private static extern void FPDF_RenderPage(IntPtr dc, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    [DllImport("pdfium")] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium")] private static extern void FPDF_CloseDocument(IntPtr document);
}
