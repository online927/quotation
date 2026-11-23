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
    public virtual string Print(byte[] pdf, string documentName) =>
        MacSupport.IsMac ? MacSupport.Print(pdf, documentName) : DirectPrinter.Print(pdf, documentName);

    /// <summary>Saves (replacing an earlier copy of the same document) without opening it.</summary>
    public virtual string Save(byte[] pdf, string fileName, string? folder = null) => SaveAndOpen(pdf, fileName, open: false, folder);

    /// <summary>Opens the folder in Explorer with the file selected.</summary>
    public virtual void ShowInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (MacSupport.IsMac) MacSupport.ShowInFinder(path);
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

    public const string WhatsAppApp = "App", WhatsAppWeb = "Web", WhatsAppInApp = "InApp";

    /// <summary>My Settings → WhatsApp (this PC): the button opens WhatsApp Web in the browser instead of the WhatsApp program.</summary>
    public bool WhatsAppOnWeb => string.Equals(config.WhatsAppOpensIn, WhatsAppWeb, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Round 36, My Settings → WhatsApp "Inside this app": WhatsApp Web in the app's own WhatsApp window (signed in once with
    /// the phone's QR code, kept apart from the person's browser). The window is Edge or Chrome in app mode with the app's own
    /// profile folder; without either, WhatsApp Web opens in the browser.
    /// </summary>
    public bool WhatsAppInsideApp => string.Equals(config.WhatsAppOpensIn, WhatsAppInApp, StringComparison.OrdinalIgnoreCase);

    /// <summary>The hint after the chat opened, for this PC's choice.</summary>
    public string WhatsAppHintNow(bool hasPhone) => WhatsAppInsideApp
        ? hasPhone ? "PDF copied. WhatsApp is opening in its own window: press Ctrl+V, then Enter."
                   : "PDF copied. WhatsApp is opening in its own window: pick the chat, press Ctrl+V, then Enter."
        : WhatsAppHint(WhatsAppOnWeb, hasPhone);

    /// <summary>The error when the chat could not be opened, for this PC's choice.</summary>
    public string WhatsAppErrorNow(string path) => WhatsAppError(WhatsAppOnWeb || WhatsAppInsideApp, path);

    /// <summary>The folder that keeps the WhatsApp window's sign-in (this PC, this Windows / Mac user).</summary>
    public static string WhatsAppWindowProfile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TSQuotation", "whatsapp-window");

    /// <summary>Edge or Chrome on this PC (for the app's own WhatsApp window), or null.</summary>
    public static string? AppWindowBrowser()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                candidates.Add(Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
                candidates.Add(Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
            candidates.Add("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The arguments that open <paramref name="url"/> in the app's own WhatsApp window.</summary>
    public static string AppWindowArguments(string url, string profile) =>
        $"--app=\"{url}\" --user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check --window-size=1100,800";

    /// <summary>Opens a WhatsApp Web address in the app's own window (or the browser when there is no Edge / Chrome).</summary>
    protected virtual void OpenWhatsAppUrl(string webUrl)
    {
        if (WhatsAppInsideApp && AppWindowBrowser() is { } browser)
        {
            Directory.CreateDirectory(WhatsAppWindowProfile);
            Process.Start(new ProcessStartInfo(browser, AppWindowArguments(webUrl, WhatsAppWindowProfile)) { UseShellExecute = false });
            return;
        }
        Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
    }

    /// <summary>The chat address for this PC's choice.</summary>
    private string ChatUrl(string? phone, string message) => WhatsAppUrl(WhatsAppOnWeb || WhatsAppInsideApp, phone, message);

    /// <summary>The customer's chat with the message typed: the WhatsApp program (whatsapp://) or WhatsApp Web (browser).</summary>
    public static string WhatsAppUrl(bool web, string? phone, string message) =>
        (web ? "https://web.whatsapp.com/send?" : "whatsapp://send?") + (phone is { Length: > 0 } p ? "phone=" + p + "&" : "")
        + "text=" + Uri.EscapeDataString(message);

    /// <summary>What to do next, after the chat opened.</summary>
    public static string WhatsAppHint(bool web, bool hasPhone) => (web, hasPhone) switch
    {
        (true, true) => "PDF copied. WhatsApp Web is opening in your browser: press Ctrl+V, then Enter.",
        (true, false) => "PDF copied. WhatsApp Web is opening in your browser: pick the chat, press Ctrl+V, then Enter.",
        (false, true) => "PDF copied. In WhatsApp press Ctrl+V, then Enter.",
        _ => "PDF copied. WhatsApp opened: pick the chat, press Ctrl+V, then Enter.",
    };

    /// <summary>The chat could not be opened (the PDF is saved all the same).</summary>
    public static string WhatsAppError(bool web, string path) => web
        ? "The browser could not be opened for WhatsApp Web. The PDF is saved: " + path
        : "WhatsApp could not be opened. Is it installed on this PC? (Or choose WhatsApp Web in My Settings.) The PDF is saved: " + path;

    /// <summary>
    /// WhatsApp (E2): the PDF is put on the clipboard (as if copied in File Explorer) and the customer's chat opens (or the
    /// chat list when there is no phone number) with the message typed, in the WhatsApp program or in WhatsApp Web (My
    /// Settings). The user pastes (Ctrl+V) and presses Enter. WhatsApp does not let other programs attach or send by
    /// themselves.
    /// </summary>
    /// <summary>Round 36: WhatsApp opens with a text typed (the evening summary); the person picks the chat and presses Enter.</summary>
    public virtual bool OpenWhatsAppText(string message)
    {
        try
        {
            OpenWhatsAppUrl(ChatUrl(null, message));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

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
            else if (MacSupport.IsMac) MacSupport.CopyFileToClipboard(pdfPath);
            OpenWhatsAppUrl(ChatUrl(phone, message));
            return true;
        }
        catch (Exception)
        {
            return false; // WhatsApp not installed, or no browser
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
