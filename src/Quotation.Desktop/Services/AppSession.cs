using Quotation.ApiClient;

namespace Quotation.Desktop.Services;

/// <summary>Holds the per-session state shared by view models.</summary>
public sealed class AppSession(ClientConfig config, Func<string, QuotationApiClient> clientFactory)
{
    public ClientConfig Config { get; } = config;
    public QuotationApiClient Api { get; private set; } = clientFactory(config.ServerUrl);

    public void Reconnect(string serverUrl)
    {
        Config.ServerUrl = serverUrl;
        Api = clientFactory(serverUrl);
    }

    public bool IsAdmin => Api.CurrentUser?.Role == Core.Domain.UserRole.Admin;
    /// <summary>Makes quotations and proforma invoices: everybody but the administrator.</summary>
    public bool MakesDocuments => Api.CurrentUser?.MakesDocuments == true;
    /// <summary>Proforma invoices are switched on (Settings → Quotation), as last read from the server.</summary>
    public bool ProformaEnabled => Settings?.Quotation.ProformaEnabled == true;
    /// <summary>The background picture of this PC was chosen or removed (My Settings).</summary>
    public event EventHandler? BackgroundPictureChanged;

    /// <summary>The background picture of this PC, if one is chosen and still there.</summary>
    public string? BackgroundPicturePath =>
        Config.BackgroundPicture is { Length: > 0 } p && File.Exists(p) ? p : null;

    /// <summary>
    /// Keeps a copy of <paramref name="source"/> next to this PC's settings and shows it behind the pages; returns an
    /// error message, or null. The picture stays on this PC (it is not sent to the server).
    /// </summary>
    public string? SetBackgroundPicture(string source)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists) return "The picture was not found.";
            if (info.Length > 20 * 1024 * 1024) return "The picture is larger than 20 MB.";
            var ext = info.Extension.ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp")) return "Choose a JPG, PNG, BMP or WEBP picture.";
            using (var test = File.OpenRead(source)) _ = new Avalonia.Media.Imaging.Bitmap(test);
            var folder = Path.GetDirectoryName(Config.FilePath ?? ClientConfig.DefaultPath)!;
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "background" + ext);
            foreach (var old in Directory.GetFiles(folder, "background.*")) if (old != target) File.Delete(old);
            File.Copy(source, target, overwrite: true);
            Config.BackgroundPicture = target;
            Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or InvalidOperationException)
        {
            return "The picture could not be used: " + ex.Message;
        }
        BackgroundPictureChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    public void RemoveBackgroundPicture()
    {
        try
        {
            if (Config.BackgroundPicture is { Length: > 0 } p && File.Exists(p)) File.Delete(p);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file stays, but it is no longer shown.
        }
        Config.BackgroundPicture = "";
        try { Config.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        BackgroundPictureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Settings were read again or saved (the menu shows or hides proforma invoices).</summary>
    public event EventHandler? SettingsChanged;

    public DocumentLauncher Documents { get; set; } = new(config);

    /// <summary>Shows the screens light or dark and remembers it on this PC for the login screen.</summary>
    public void ApplyTheme(string theme)
    {
        ThemeService.Apply(theme);
        if (Config.Theme == theme) return;
        Config.Theme = theme;
        try
        {
            Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the login screen's look depends on it.
        }
    }

    /// <summary>Theme, text size, spacing and animations of the signed-in user.</summary>
    public void ApplyAppearance(Contracts.UserDto user)
    {
        ApplyTheme(user.Theme);
        ThemeService.ApplyDisplay(user.TextSize, user.Spacing, user.ReduceMotion);
    }

    /// <summary>Saves text size, spacing or animations on the server (they follow the user to every PC) and shows them.</summary>
    public async Task SetDisplayAsync(string? textSize = null, string? spacing = null, bool? reduceMotion = null)
    {
        var me = await Api.SetMyPreferencesAsync(new Contracts.MyPreferencesDto(TextSize: textSize, Spacing: spacing, ReduceMotion: reduceMotion));
        Api.UpdateCurrentUser(me);
        ThemeService.ApplyDisplay(me.TextSize, me.Spacing, me.ReduceMotion);
    }

    /// <summary>Saves the signed-in user's light/dark choice on the server (it follows them to every PC).</summary>
    public async Task SetThemeAsync(string theme)
    {
        ApplyTheme(theme);
        Api.UpdateCurrentUser(await Api.SetMyPreferencesAsync(new Contracts.MyPreferencesDto(Theme: theme)));
    }

    /// <summary>Settings cached for the session (company state, tax presentation, defaults).</summary>
    public Contracts.AllSettingsDto? Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private Contracts.AllSettingsDto? _settings;

    /// <summary>Keeps the company name (Settings → Company) on this PC for the sign-in screen.</summary>
    private void RememberCompanyName(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name == Config.CompanyName) return;
        Config.CompanyName = name;
        try
        {
            Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the sign-in screen uses it.
        }
    }

    public async Task<Contracts.AllSettingsDto> GetSettingsAsync(bool refresh = false)
    {
        if (Settings is null || refresh) Settings = await Api.SettingsAsync();
        RememberCompanyName(Settings.Company.CompanyName);
        return Settings;
    }
}
