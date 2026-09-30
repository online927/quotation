using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Server.Infrastructure;

namespace Quotation.Server.Services;

/// <summary>
/// Typed access to settings stored in the database. Secrets (API key, OAuth data) are stored
/// encrypted under separate keys and are never returned to clients.
/// </summary>
public sealed class SettingsService(IServiceScopeFactory scopes, ISecretProtector protector, TimeProvider clock)
{
    public const string CompanyKey = "company";
    public const string QuotationKey = "quotation";
    public const string TallyKey = "tally";
    public const string AiKey = "ai";
    public const string GmailKey = "gmail";
    public const string SecretAiApiKey = "secret:ai.apikey";
    public const string SecretGmailClient = "secret:gmail.client";
    public const string SecretGmailToken = "secret:gmail.token";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, object> _cache = new();

    public event Action<string>? Changed;

    public CompanySettings Company => Get<CompanySettings>(CompanyKey);
    public QuotationSettings Quotation => Get<QuotationSettings>(QuotationKey);
    public TallySettings Tally => Get<TallySettings>(TallyKey);
    public AiSettings Ai => Get<AiSettings>(AiKey);
    public GmailSettings Gmail => Get<GmailSettings>(GmailKey);

    public T Get<T>(string key) where T : class, new()
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) return Clone((T)cached);
        }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var entry = db.Settings.AsNoTracking().FirstOrDefault(s => s.Key == key);
        var value = entry is null ? new T() : JsonSerializer.Deserialize<T>(entry.Json, Json) ?? new T();
        lock (_gate) _cache[key] = value;
        return Clone(value);
    }

    public async Task SaveAsync<T>(string key, T value, string user, CancellationToken ct = default) where T : class
    {
        await WriteAsync(key, JsonSerializer.Serialize(value, Json), user, ct);
        lock (_gate) _cache[key] = value;
        Changed?.Invoke(key);
    }

    public string? GetSecret(string key)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var entry = db.Settings.AsNoTracking().FirstOrDefault(s => s.Key == key);
        return entry is null || entry.Json.Length == 0 ? null : protector.Unprotect(entry.Json);
    }

    public bool HasSecret(string key)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        return db.Settings.AsNoTracking().Any(s => s.Key == key && s.Json != "");
    }

    public async Task SetSecretAsync(string key, string? value, string user, CancellationToken ct = default)
    {
        await WriteAsync(key, string.IsNullOrEmpty(value) ? "" : protector.Protect(value), user, ct);
        Changed?.Invoke(key);
    }

    public AllSettingsDto GetAllForClient()
    {
        var ai = Ai;
        ai.ApiKey = null;
        ai.ApiKeyConfigured = HasSecret(SecretAiApiKey);
        var gmail = Gmail;
        gmail.ClientSecretJson = null;
        gmail.ClientSecretConfigured = HasSecret(SecretGmailClient);
        return new AllSettingsDto { Company = Company, Quotation = Quotation, Tally = Tally, Ai = ai, Gmail = gmail };
    }

    public async Task SaveAllFromClientAsync(AllSettingsDto dto, string user, CancellationToken ct)
    {
        await SaveAsync(CompanyKey, dto.Company, user, ct);
        await SaveAsync(QuotationKey, dto.Quotation, user, ct);
        await SaveAsync(TallyKey, dto.Tally, user, ct);

        // Write-only secrets: only replaced when a new value is supplied.
        if (!string.IsNullOrWhiteSpace(dto.Ai.ApiKey)) await SetSecretAsync(SecretAiApiKey, dto.Ai.ApiKey.Trim(), user, ct);
        if (!string.IsNullOrWhiteSpace(dto.Gmail.ClientSecretJson)) await SetSecretAsync(SecretGmailClient, dto.Gmail.ClientSecretJson.Trim(), user, ct);

        var ai = dto.Ai; ai.ApiKey = null; ai.ApiKeyConfigured = false;
        var gmail = dto.Gmail; gmail.ClientSecretJson = null; gmail.ClientSecretConfigured = false;
        gmail.ConnectedAccount = Gmail.ConnectedAccount; // managed by the OAuth flow, not the client
        await SaveAsync(AiKey, ai, user, ct);
        await SaveAsync(GmailKey, gmail, user, ct);
    }

    private async Task WriteAsync(string key, string json, string user, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var entry = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (entry is null)
        {
            entry = new SettingEntry { Key = key };
            db.Settings.Add(entry);
        }
        entry.Json = json;
        entry.UpdatedBy = user;
        entry.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}
