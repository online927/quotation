using System.Diagnostics;

namespace Quotation.Desktop.Services;

/// <summary>Saves a downloaded PDF locally and opens it with the default viewer.</summary>
public class DocumentLauncher(ClientConfig config)
{
    public string Folder => string.IsNullOrWhiteSpace(config.PdfFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Quotations")
        : config.PdfFolder;

    public virtual string SaveAndOpen(byte[] pdf, string fileName, bool open = true)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, fileName);
        File.WriteAllBytes(path, pdf);
        if (open) Open(path);
        return path;
    }

    public virtual void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default PDF viewer: the file is still saved and its path shown to the user.
        }
    }
}
