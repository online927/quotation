using System.Text.Json;

namespace Quotation.Mobile.Services;

/// <summary>Kept on the phone: the server address, the last user, the sign-in (so the app opens signed in) and the theme.</summary>
public sealed class PhoneSettings
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Theme { get; set; } = Quotation.Contracts.AppTheme.StartTheme;
    public string? Token { get; set; }
    public DateTime? TokenExpiresUtc { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string FilePath { get; set; } = DefaultPath;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "tsq-phone.json");

    public static PhoneSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<PhoneSettings>(File.ReadAllText(path)) is { } s)
            {
                s.FilePath = path;
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new PhoneSettings { FilePath = path };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>"192.168.1.20" → "http://192.168.1.20:5080" (the server's usual port); a full address is kept.</summary>
    public static string NormalizeServer(string? typed)
    {
        var s = (typed ?? "").Trim().TrimEnd('/');
        if (s.Length == 0) return "";
        if (!s.Contains("://")) s = "http://" + s;
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.IsDefaultPort && !s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..].Contains(':'))
            s = $"{uri.Scheme}://{uri.Host}:5080{uri.AbsolutePath.TrimEnd('/')}";
        return s;
    }
}
