using System.Diagnostics;

namespace Quotation.Desktop.Services;

/// <summary>Saves a downloaded PDF locally and opens it with the default viewer.</summary>
public class DocumentLauncher(ClientConfig config)
{
    public string Folder => string.IsNullOrWhiteSpace(config.PdfFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.Create), "Quotations")
        : config.PdfFolder;

    /// <summary>Proforma invoices: their own folder, by default "Proforma Invoices" inside the quotation folder.</summary>
    public string ProformaFolder => string.IsNullOrWhiteSpace(config.ProformaPdfFolder)
        ? Path.Combine(Folder, "Proforma Invoices")
        : config.ProformaPdfFolder;

    public string FolderFor(bool proforma) => proforma ? ProformaFolder : Folder;

    public virtual string SaveAndOpen(byte[] pdf, string fileName, bool open = true, string? folder = null)
    {
        folder ??= Folder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, pdf);
        if (open) Open(path);
        return path;
    }

    /// <summary>Ctrl+P: prints the PDF straight to the Windows default printer (no print window); returns the printer's name.</summary>
    public virtual string Print(byte[] pdf, string documentName) => DirectPrinter.Print(pdf, documentName);

    /// <summary>Saves (replacing an earlier copy of the same document) without opening it.</summary>
    public virtual string Save(byte[] pdf, string fileName, string? folder = null) => SaveAndOpen(pdf, fileName, open: false, folder);

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

    /// <summary>
    /// WhatsApp (E2): the PDF is put on the clipboard (as if copied in File Explorer) and WhatsApp Desktop opens in the
    /// customer's chat (or without a chat when there is no phone number) with the message typed. The user pastes (Ctrl+V)
    /// and presses Enter. WhatsApp does not let other programs attach or send by themselves.
    /// </summary>
    public virtual bool ShareOnWhatsApp(string pdfPath, string? phone, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var clip = Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -Command \"Set-Clipboard -LiteralPath '{pdfPath.Replace("'", "''")}'\"")
                { CreateNoWindow = true, UseShellExecute = false });
                clip?.WaitForExit(5000);
            }
            var url = "whatsapp://send?" + (phone is { Length: > 0 } p ? "phone=" + p + "&" : "") + "text=" + Uri.EscapeDataString(message);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false; // WhatsApp Desktop not installed
        }
    }

    /// <summary>Indian mobile/landline as WhatsApp expects it: 91 + 10 digits; null when it does not look like a number.</summary>
    public static string? WhatsAppNumber(string? phone)
    {
        var first = (phone ?? "").Split(',', '/', ';')[0];
        var digits = new string(first.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
        if (digits.Length == 10) return "91" + digits;
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal)) return digits;
        return null;
    }

    /// <summary>Runs a downloaded client installer quietly (the app restarts itself when it is done).</summary>
    public virtual bool RunInstaller(string setupPath, string serverUrl)
    {
        try
        {
            // The setup refuses to run while this program is open, so a hidden PowerShell waits for it to close first.
            var pid = Environment.ProcessId;
            static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
            var args = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SERVER={serverUrl}";
            var script = $"Wait-Process -Id {pid} -Timeout 60 -ErrorAction SilentlyContinue; " +
                         $"Start-Process -FilePath {Quote(setupPath)} -ArgumentList {Quote(args)}";
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-Command", script },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
