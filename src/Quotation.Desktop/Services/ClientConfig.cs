using System.Text.Json;

namespace Quotation.Desktop.Services;

/// <summary>Per-PC client settings stored in %AppData%\TSQuotation\client.json. Contains no secrets.</summary>
public sealed class ClientConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string LastUsername { get; set; } = "";
    public string PdfFolder { get; set; } = "";
    /// <summary>Where "Save as PDF" puts proforma invoices; empty = "Proforma Invoices" inside <see cref="PdfFolder"/>.</summary>
    public string ProformaPdfFolder { get; set; } = "";
    /// <summary>Light/dark of the last user on this PC, used for the login screen.</summary>
    public string Theme { get; set; } = Quotation.Contracts.AppTheme.StartTheme;
    /// <summary>The company name from Settings → Company, remembered for the sign-in screen on this PC.</summary>
    public string CompanyName { get; set; } = "";
    /// <summary>The side menu is folded to icons on this PC.</summary>
    public bool SidebarCollapsed { get; set; }
    /// <summary>
    /// Quotation screen, gear next to Items: after a line is changed, Enter goes on to the next line: "Off", "Line" (the whole
    /// line), or one or more columns top to bottom ("Rate", or "Rate,Discount"; also "Name", "Description", "Quantity").
    /// </summary>
    public string LineStepMode { get; set; } = "Off";

    /// <summary>Gear: after changing a line, go to the line above instead of below.</summary>
    public bool LineStepUp { get; set; }

    /// <summary>Gear: hide the HSN/SAC and GST columns of the item list on this PC's screen (the PDF keeps them).</summary>
    public bool HideHsnGst { get; set; }
    /// <summary>
    /// Where the WhatsApp button opens the customer's chat on this PC: "App" (the WhatsApp program) or "Web" (WhatsApp Web in
    /// the browser).
    /// </summary>
    public string WhatsAppOpensIn { get; set; } = DocumentLauncher.WhatsAppApp;
    /// <summary>How selected text looks in typing boxes (My Settings > Selected text): see <see cref="TextSelectionLook"/>.</summary>
    public string TextSelection { get; set; } = TextSelectionLook.SoftBlue;

    /// <summary>Sharp text (My Settings, this PC): ClearType-style letters and slightly bigger small text; off = the old look.</summary>
    public bool SharpText { get; set; } = true;
    /// <summary>A picture behind the pages on this PC (a copy kept next to this file); empty = none.</summary>
    public string BackgroundPicture { get; set; } = "";

    /// <summary>Where this config is saved; not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "TSQuotation", "client.json");

    public static ClientConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(path)) ?? new();
                loaded.FilePath = path;
                return loaded;
            }
        }
        catch (Exception)
        {
            // Corrupt config: fall back to defaults rather than refusing to start.
        }
        // First run on this PC: take the installer's default server address if present.
        var defaults = Path.Combine(AppContext.BaseDirectory, "client.defaults.json");
        try
        {
            if (File.Exists(defaults))
            {
                var d = JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(defaults)) ?? new();
                return new ClientConfig { ServerUrl = d.ServerUrl, FilePath = path };
            }
        }
        catch (Exception)
        {
            // Ignore a damaged defaults file.
        }
        return new ClientConfig { FilePath = path };
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
