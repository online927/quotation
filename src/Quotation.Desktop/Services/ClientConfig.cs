using System.Text.Json;

namespace Quotation.Desktop.Services;

/// <summary>Per-PC client settings stored in %AppData%\TSQuotation\client.json. Contains no secrets.</summary>
public sealed class ClientConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string LastUsername { get; set; } = "";
    public string PdfFolder { get; set; } = "";

    /// <summary>Where this config is saved; not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TSQuotation", "client.json");

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
        return new ClientConfig { FilePath = path };
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
