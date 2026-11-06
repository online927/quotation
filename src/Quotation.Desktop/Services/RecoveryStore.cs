using System.Text.Json;
using Quotation.ApiClient;
using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>The quotation someone was working on, kept on this PC while it had unsaved changes.</summary>
public sealed record RecoverySnapshot(string User, DateTime SavedAt, QuotationDto Quotation);

/// <summary>
/// Unsaved work kept on the PC (next to the client settings, one file per user) every 30 seconds, so a power cut or a
/// crash does not lose it. Nothing goes to the server and no number is used until the user saves.
/// </summary>
public sealed class RecoveryStore(string folder)
{
    public static RecoveryStore For(ClientConfig config) =>
        new(Path.Combine(Path.GetDirectoryName(config.FilePath) is { Length: > 0 } dir ? dir : AppContext.BaseDirectory, "recovery"));

    private string FileFor(string user) =>
        Path.Combine(folder, string.Concat(user.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".json");

    public void Save(RecoverySnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var file = FileFor(snapshot.User);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(snapshot, QuotationApiClient.JsonOptions));
            File.Move(file + ".tmp", file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    public RecoverySnapshot? Load(string user)
    {
        try
        {
            var file = FileFor(user);
            return File.Exists(file) ? JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllText(file), QuotationApiClient.JsonOptions) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Delete(string user)
    {
        try { File.Delete(FileFor(user)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
