using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using Quotation.Contracts;
using Quotation.Data.Entities;
using Quotation.Server.Auth;

namespace Quotation.Server.Services;

/// <summary>
/// Personal seals: each user (not administrators) can have their own seal picture and 4-digit seal PIN, set by an
/// administrator in Settings → Users. A PDF is sealed only on request, with that user's PIN, and gets that user's picture.
/// Administrators do not seal. After five wrong PINs the seal is blocked for that user for 15 minutes (a 4-digit code
/// could otherwise be found by trying). The former company seal picture (seal\seal.img) is kept only so it can be given
/// to a user without uploading it again.
/// </summary>
public sealed class SealService(IOptions<ServerOptions> options, TimeProvider clock)
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BlockTime = TimeSpan.FromMinutes(15);
    private const int MaxImageBytes = 5 * 1024 * 1024;

    private readonly string _folder = Path.Combine(options.Value.ResolveDataDirectory(), "seal");
    private readonly ConcurrentDictionary<string, (int Failures, DateTime? BlockedUntilUtc)> _attempts = new(StringComparer.OrdinalIgnoreCase);

    private string FormerCompanyPath => Path.Combine(_folder, "seal.img");
    private string UserPath(int userId) => Path.Combine(_folder, $"user-{userId}.img");

    /// <summary>The company seal picture from before personal seals (can be given to a user).</summary>
    public bool HasFormerCompanySeal => File.Exists(FormerCompanyPath);

    public byte[]? ReadImage(int userId) => File.Exists(UserPath(userId)) ? File.ReadAllBytes(UserPath(userId)) : null;

    /// <summary>A seal picture to show where seals are printed (Settings → Company): any user's, or the former company seal.</summary>
    public byte[]? AnyImage()
    {
        if (!Directory.Exists(_folder)) return null;
        var file = Directory.EnumerateFiles(_folder, "user-*.img").FirstOrDefault() ?? (HasFormerCompanySeal ? FormerCompanyPath : null);
        return file is null ? null : File.ReadAllBytes(file);
    }

    /// <summary>Checks a picture (PNG with a transparent background looks best; JPG works); null when it can be used.</summary>
    public static string? Validate(byte[] bytes)
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
        return null;
    }

    /// <summary>Stores a user's seal picture; returns an error message, or null when saved.</summary>
    public async Task<string?> SaveImageAsync(int userId, byte[] bytes, CancellationToken ct)
    {
        if (Validate(bytes) is { } error) return error;
        Directory.CreateDirectory(_folder);
        var temp = UserPath(userId) + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, UserPath(userId), overwrite: true);
        return null;
    }

    /// <summary>Gives a user the former company seal picture.</summary>
    public bool CopyFormerCompanySeal(int userId)
    {
        if (!HasFormerCompanySeal) return false;
        Directory.CreateDirectory(_folder);
        File.Copy(FormerCompanyPath, UserPath(userId), overwrite: true);
        return true;
    }

    public void DeleteImage(int userId)
    {
        if (File.Exists(UserPath(userId))) File.Delete(UserPath(userId));
    }

    public static string? ValidatePin(string pin) =>
        pin.Length == 4 && pin.All(char.IsAsciiDigit) ? null : "The seal PIN must be exactly 4 digits.";

    public static string HashPin(string pin) => PasswordHasher.Hash(pin);

    /// <summary>Checks that this user has a seal and typed its PIN; returns their seal picture, or throws with the reason.</summary>
    public byte[] Authorize(UserContext user, AppUser? account, string? pin)
    {
        if (user.IsAdmin)
            throw new QuotationException(403, ApiErrorCodes.Forbidden, "Administrators do not seal PDFs.");
        var image = account is { HasSealImage: true } ? ReadImage(account.Id) : null;
        if (account is null || image is null || account.SealPinHash is null)
            throw new QuotationException(409, ApiErrorCodes.InvalidState,
                "You have no seal yet: an administrator adds your seal picture and PIN in Settings → Users.");

        var now = clock.GetUtcNow().UtcDateTime;
        var state = _attempts.GetValueOrDefault(user.UserName);
        if (state.BlockedUntilUtc is { } until && until > now)
            throw new QuotationException(429, ApiErrorCodes.SealBlocked,
                $"Too many wrong seal PINs. Try again after {TimeZoneInfo.ConvertTimeFromUtc(until, clock.LocalTimeZone):HH:mm}.");
        if (pin is null || !PasswordHasher.Verify(pin, account.SealPinHash))
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

    /// <summary>A new PIN set by an administrator clears that user's wrong tries.</summary>
    public void ResetAttempts(string userName) => _attempts.TryRemove(userName, out _);
}
