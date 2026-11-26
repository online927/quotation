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

    /// <summary>Kept only so old settings files read; Round 39 (GU-1) always goes down to the next line.</summary>
    public bool LineStepUp { get; set; }

    /// <summary>Kept only so old settings files read; Round 39 (GR-1) has three separate switches below.</summary>
    public bool HideHsnGst { get; set; }

    /// <summary>Round 39 (GR-1), gear before the date: hide the GST column on this PC's screen (the PDF keeps it). On at first.</summary>
    public bool HideGst { get; set; } = true;

    /// <summary>Round 39 (GR-1): hide the HSN/SAC column on this PC's screen. On at first.</summary>
    public bool HideHsn { get; set; } = true;

    /// <summary>Round 39 (GR-1): hide the "per" (unit) column on this PC's screen. On at first.</summary>
    public bool HidePer { get; set; } = true;

    /// <summary>The one-time "hide GST, HSN and per" was applied to this PC's saved settings.</summary>
    public bool HideColumnsByDefaultApplied { get; set; }

    /// <summary>Round 39 (IE-1), My Settings → "Editing a line": true = in the list itself; false = in the boxes below (old way).</summary>
    public bool EditLineInList { get; set; }
    /// <summary>
    /// Where the WhatsApp button opens the customer's chat on this PC: "App" (the WhatsApp program) or "Web" (WhatsApp Web in
    /// the browser).
    /// </summary>
    public string WhatsAppOpensIn { get; set; } = DocumentLauncher.WhatsAppApp;
    /// <summary>How selected text looks in typing boxes (My Settings > Selected text): see <see cref="TextSelectionLook"/>.</summary>
    public string TextSelection { get; set; } = TextSelectionLook.SoftBlue;

    /// <summary>Kept only so old settings files read; Round 39 (SX-1): sharp text is always on.</summary>
    public bool SharpText { get; set; } = true;

    /// <summary>Kept only so old settings files read; Round 39 (SX-1): the click sounds are always on.</summary>
    public bool Silent { get; set; } = true;

    /// <summary>Round 36: the day the evening summary was last shown on this PC (yyyy-MM-dd).</summary>
    public string? EveningSummaryShownDay { get; set; }

    /// <summary>
    /// My Settings → Delivery time on items (this PC, Round 35): a Delivery box next to Description. On at first (user:
    /// "Delivery box, default on"); PCs that had it off get it on once (<see cref="DeliveryOnByDefaultApplied"/>), and can
    /// switch it off again.
    /// </summary>
    public bool DeliveryTimeOnItems { get; set; } = true;

    /// <summary>Round 37, My Settings → "Item rows (this PC)": Bigger (true, the default) or Normal.</summary>
    public bool BiggerItemRows { get; set; } = true;

    /// <summary>The one-time "Delivery box on" was applied to this PC's saved settings.</summary>
    public bool DeliveryOnByDefaultApplied { get; set; }

    /// <summary>Kept only so old settings files read; Round 38 (DM-1) no longer remembers typed delivery times (cleared on load).</summary>
    public List<string> RecentDeliveryTimes { get; set; } = [];
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
                if (!loaded.DeliveryOnByDefaultApplied)
                {
                    // Round 36: the Delivery box is on by default, also on PCs where it was off before; once only.
                    loaded.DeliveryTimeOnItems = true;
                    loaded.DeliveryOnByDefaultApplied = true;
                    try { loaded.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                if (!loaded.HideColumnsByDefaultApplied)
                {
                    // Round 39 (GR-1): GST, HSN and per are hidden on the screen by default, also on PCs set up before; once only.
                    loaded.HideGst = loaded.HideHsn = loaded.HidePer = true;
                    loaded.HideColumnsByDefaultApplied = true;
                    try { loaded.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                if (loaded.RecentDeliveryTimes.Count > 0)
                {
                    // Round 38 (DM-1): typed delivery times are no longer remembered; the old ones are cleared once.
                    loaded.RecentDeliveryTimes = [];
                    try { loaded.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
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
                return new ClientConfig { ServerUrl = d.ServerUrl, FilePath = path, DeliveryOnByDefaultApplied = true, HideColumnsByDefaultApplied = true };
            }
        }
        catch (Exception)
        {
            // Ignore a damaged defaults file.
        }
        return new ClientConfig { FilePath = path, DeliveryOnByDefaultApplied = true, HideColumnsByDefaultApplied = true };
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
