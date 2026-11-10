namespace Quotation.Server;

/// <summary>Bound from the "Server" section of appsettings.json.</summary>
public sealed class ServerOptions
{
    /// <summary>Folder for the database, PDFs, logs and backups. Default: %ProgramData%\TSQuotation.</summary>
    public string DataDirectory { get; set; } = "";
    /// <summary>Session lifetime for desktop clients.</summary>
    public int SessionHours { get; set; } = 12;
    /// <summary>Folder with the client installer the server hands out (TSQuotation-Setup.exe + version.txt). Default: "client" next to the server.</summary>
    public string? ClientUpdateFolder { get; set; }
    public string ResolveClientUpdateFolder() =>
        string.IsNullOrWhiteSpace(ClientUpdateFolder) ? Path.Combine(AppContext.BaseDirectory, "client") : ClientUpdateFolder;
    /// <summary>Password for the initial "admin" user (first start only). The user must change it.</summary>
    public string BootstrapAdminPassword { get; set; } = "admin";
    /// <summary>
    /// Administrators make no quotations or proforma invoices (they open, cancel and delete them). True lets the
    /// administrator make documents again, for an office with nobody else to make them.
    /// </summary>
    public bool AdministratorsMakeDocuments { get; set; }
    /// <summary>Disable background workers (used by tests).</summary>
    public bool EnableBackgroundWorkers { get; set; } = true;

    public string ResolveDataDirectory()
    {
        var dir = string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TSQuotation")
            : Environment.ExpandEnvironmentVariables(DataDirectory);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
