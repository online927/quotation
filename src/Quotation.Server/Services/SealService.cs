using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using Quotation.Contracts;
using Quotation.Server.Auth;

namespace Quotation.Server.Services;

/// <summary>
/// The company seal: one image on the server (uploaded by an administrator) and a 4-digit seal PIN. A PDF is sealed only
/// on request, by an administrator or, when allowed in Settings, by any user, and only with the right PIN. After five wrong
/// PINs the seal is blocked for that user for 15 minutes (a 4-digit code could otherwise be found by trying).
/// </summary>
public sealed class SealService(IOptions<ServerOptions> options, SettingsService settings, TimeProvider clock)
{
    public const string SecretPin = "secret:seal.pin";
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BlockTime = TimeSpan.FromMinutes(15);
    private const int MaxImageBytes = 5 * 1024 * 1024;

    private readonly string _folder = Path.Combine(options.Value.ResolveDataDirectory(), "seal");
    private readonly ConcurrentDictionary<string, (int Failures, DateTime? BlockedUntilUtc)> _attempts = new(StringComparer.OrdinalIgnoreCase);

    private string ImagePath => Path.Combine(_folder, "seal.img");

    public bool HasImage => File.Exists(ImagePath);
    public bool HasPin => settings.HasSecret(SecretPin);

    public byte[]? ReadImage() => HasImage ? File.ReadAllBytes(ImagePath) : null;

    /// <summary>Stores a new seal image (PNG with a transparent background looks best; JPG works).</summary>
    public async Task<string?> SaveImageAsync(byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length == 0) return "The file is empty.";
        if (bytes.Length > MaxImageBytes) return "The image is larger than 5 MB.";
        try
        {
            using var ms = new MemoryStream(bytes);
            using var img = XImage.FromStream(ms);
            if (img.PixelWidth < 20 || img.PixelHeight < 20) return "The image is too small.";
        }
        catch (Exception)
        {
            return "This is not a PNG or JPG image.";
        }
        Directory.CreateDirectory(_folder);
        var temp = ImagePath + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, ImagePath, overwrite: true);
        return null;
    }

    public void DeleteImage()
    {
        if (File.Exists(ImagePath)) File.Delete(ImagePath);
    }

    public static string? ValidatePin(string pin) =>
        pin.Length == 4 && pin.All(char.IsAsciiDigit) ? null : "The seal PIN must be exactly 4 digits.";

    public Task SetPinAsync(string pin, string user, CancellationToken ct) => settings.SetSecretAsync(SecretPin, PasswordHasher.Hash(pin), user, ct);

    /// <summary>Checks permission and PIN; returns the seal image, or throws with the reason.</summary>
    public byte[] Authorize(UserContext user, string? pin)
    {
        if (!user.IsAdmin && !settings.Quotation.AllUsersCanSeal)
            throw new QuotationException(403, ApiErrorCodes.Forbidden, "Only an administrator can put the company seal on PDFs (see Settings → Quotation).");
        var image = ReadImage() ?? throw new QuotationException(409, ApiErrorCodes.InvalidState,
            "No seal image yet: an administrator adds it in Settings → Company → Seal.");
        var hash = settings.GetSecret(SecretPin) ?? throw new QuotationException(409, ApiErrorCodes.InvalidState,
            "No seal PIN yet: an administrator sets it in Settings → Company → Seal.");

        var now = clock.GetUtcNow().UtcDateTime;
        var state = _attempts.GetValueOrDefault(user.UserName);
        if (state.BlockedUntilUtc is { } until && until > now)
            throw new QuotationException(429, ApiErrorCodes.SealBlocked,
                $"Too many wrong seal PINs. Try again after {TimeZoneInfo.ConvertTimeFromUtc(until, clock.LocalTimeZone):HH:mm}.");
        if (pin is null || !PasswordHasher.Verify(pin, hash))
        {
            var failures = (state.BlockedUntilUtc is null ? state.Failures : 0) + 1;
            var blocked = failures >= MaxAttempts ? now + BlockTime : (DateTime?)null;
            _attempts[user.UserName] = (blocked is null ? failures : 0, blocked);
            throw new QuotationException(403, ApiErrorCodes.WrongPin, blocked is null
                ? $"Wrong seal PIN ({MaxAttempts - failures} tries left)."
                : $"Wrong seal PIN. The seal is blocked for {BlockTime.TotalMinutes:0} minutes.");
        }
        _attempts.TryRemove(user.UserName, out _);
        return image;
    }
}
