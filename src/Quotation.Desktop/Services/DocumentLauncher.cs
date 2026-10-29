using System.Diagnostics;

namespace Quotation.Desktop.Services;

/// <summary>Saves a downloaded PDF locally and opens it with the default viewer.</summary>
public class DocumentLauncher(ClientConfig config)
{
    public string Folder => string.IsNullOrWhiteSpace(config.PdfFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.Create), "Quotations")
        : config.PdfFolder;

    public virtual string SaveAndOpen(byte[] pdf, string fileName, bool open = true)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, fileName);
        File.WriteAllBytes(path, pdf);
        if (open) Open(path);
        return path;
    }

    /// <summary>Saves (replacing an earlier copy of the same document) without opening it.</summary>
    public virtual string Save(byte[] pdf, string fileName) => SaveAndOpen(pdf, fileName, open: false);

    /// <summary>Opens the folder in Explorer with the file selected.</summary>
    public virtual void ShowInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No file manager available: the path is shown in the app.
        }
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
