using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Mobile.Services;

/// <summary>The signed-in phone: settings on the phone, the connection to the Quotation Server, the user and the company settings.</summary>
public sealed class PhoneSession(PhoneSettings settings, Func<string, QuotationApiClient> createApi, IPhoneActions phone)
{
    public PhoneSettings Settings { get; } = settings;
    public IPhoneActions Phone { get; } = phone;
    public QuotationApiClient Api { get; private set; } = createApi(string.IsNullOrEmpty(settings.ServerUrl) ? "http://localhost:5080" : settings.ServerUrl);
    public UserDto? User => Api.CurrentUser;
    public AllSettingsDto? Company { get; private set; }
    public bool IsAdmin => User?.Role == UserRole.Admin;

    /// <summary>A new connection when the server address changed.</summary>
    public void UseServer(string url)
    {
        url = PhoneSettings.NormalizeServer(url);
        if (url.Length == 0) return;
        if (Api.BaseAddress is { } now && string.Equals(now.ToString().TrimEnd('/'), url, StringComparison.OrdinalIgnoreCase)) return;
        Api = createApi(url);
        Settings.ServerUrl = url;
    }

    public async Task SignInAsync(string username, string password)
    {
        var login = await Api.LoginAsync(username, password);
        Settings.Username = username;
        Settings.Token = login.Token;
        Settings.TokenExpiresUtc = login.ExpiresUtc;
        await AfterSignInAsync();
    }

    /// <summary>Opening the app: the saved sign-in is used while it is still valid.</summary>
    public async Task<bool> ResumeAsync()
    {
        if (Settings.Token is not { Length: > 0 } token || Settings.TokenExpiresUtc is not { } expires || expires <= DateTime.UtcNow.AddMinutes(5))
            return false;
        Api.SetToken(token);
        try
        {
            Api.UpdateCurrentUser(await Api.MeAsync());
            if (Api.CurrentUser is null) return false;
            await AfterSignInAsync();
            return true;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            // Not valid any more, or the server cannot be reached (not on the office Wi-Fi): the sign-in screen.
            Api.SetToken(null);
            return false;
        }
    }

    private async Task AfterSignInAsync()
    {
        try { Company = await Api.SettingsAsync(); }
        catch (ApiException) { Company = null; }
        if (Company?.Company.CompanyName is { Length: > 0 } name) Settings.CompanyName = name;
        if (User is { } u) Settings.Theme = AppTheme.Normalize(u.Theme);
        Settings.Save();
    }

    public async Task SignOutAsync()
    {
        try { await Api.LogoutAsync(); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { }
        Api.SetToken(null);
        Settings.Token = null;
        Settings.TokenExpiresUtc = null;
        Settings.Save();
    }

    /// <summary>The quotation's PDF saved in the phone's files (for opening or sharing).</summary>
    public async Task<string> SavePdfAsync(QuotationDto q)
    {
        var bytes = await Api.QuotationPdfAsync(q.Id);
        Directory.CreateDirectory(Phone.FilesFolder);
        var path = Path.Combine(Phone.FilesFolder, PdfFileName(q));
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    /// <summary>"TSQ2627-5 Sonepar India.pdf" (a draft without a number: "Draft …").</summary>
    public static string PdfFileName(QuotationDto q)
    {
        var name = $"{q.Number ?? "Draft"} {q.Buyer.Name}".Trim();
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var clean = new string(name.Select(c => bad.Contains(c) ? ' ' : c).ToArray());
        clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > 80) clean = clean[..80];
        return clean.TrimEnd('.', ' ') + ".pdf";
    }
}
