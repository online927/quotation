using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.AI;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Gmail;

namespace Quotation.Server.Services;

public interface IGmailClientFactory
{
    /// <summary>Null when Gmail is not connected.</summary>
    IGmailClient? Create();
}

public sealed class GoogleGmailClientFactory(SettingsService settings) : IGmailClientFactory
{
    public IGmailClient? Create()
    {
        var client = settings.GetSecret(SettingsService.SecretGmailClient);
        var token = settings.GetSecret(SettingsService.SecretGmailToken);
        return string.IsNullOrWhiteSpace(client) || string.IsNullOrWhiteSpace(token) ? null : new GoogleGmailClient(client, token);
    }
}

/// <summary>Handles the OAuth connect flow. Pending states expire after 10 minutes.</summary>
public sealed class GmailConnectService(SettingsService settings, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (string RedirectUri, DateTime Expires)> _pending = new();

    public GmailConnectStartResponse Start(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            throw new QuotationException(400, ApiErrorCodes.Validation, "The redirect address must be a loopback address (http://127.0.0.1:port/).");
        var client = settings.GetSecret(SettingsService.SecretGmailClient)
                     ?? throw new QuotationException(400, ApiErrorCodes.Validation, "Paste the Google OAuth client JSON in Settings → Gmail first.");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _pending[state] = (redirectUri, clock.GetUtcNow().UtcDateTime.AddMinutes(10));
        return new GmailConnectStartResponse(GmailOAuth.BuildAuthorizationUrl(client, redirectUri, state), state);
    }

    public async Task<string> CompleteAsync(GmailConnectCompleteRequest r, Func<string, string, IGmailClient> clientFactory, string user, CancellationToken ct)
    {
        if (!_pending.TryRemove(r.State, out var pending) || pending.Expires < clock.GetUtcNow().UtcDateTime || pending.RedirectUri != r.RedirectUri)
            throw new QuotationException(400, ApiErrorCodes.Validation, "The sign-in session expired or is invalid. Please try again.");
        var client = settings.GetSecret(SettingsService.SecretGmailClient)!;
        string refreshToken;
        try
        {
            refreshToken = await GmailOAuth.ExchangeCodeAsync(client, r.Code, r.RedirectUri, ct);
        }
        catch (Exception ex) when (ex is not QuotationException)
        {
            throw new QuotationException(400, "GMAIL_OAUTH", "Google sign-in failed: " + ex.Message);
        }
        var account = await clientFactory(client, refreshToken).GetAccountEmailAsync(ct);
        await settings.SetSecretAsync(SettingsService.SecretGmailToken, refreshToken, user, ct);
        var g = settings.Gmail;
        g.ConnectedAccount = account;
        await settings.SaveAsync(SettingsService.GmailKey, g, user, ct);
        return account;
    }

    public async Task DisconnectAsync(string user, CancellationToken ct)
    {
        await settings.SetSecretAsync(SettingsService.SecretGmailToken, null, user, ct);
        var g = settings.Gmail;
        g.ConnectedAccount = null;
        await settings.SaveAsync(SettingsService.GmailKey, g, user, ct);
    }
}

/// <summary>
/// Polls the configured Gmail query and turns new messages into AI Inbox entries. Every message is
/// recorded (unique Gmail message id) before processing, so restarts and overlapping polls never
/// create duplicate quotations; failed messages are retried a limited number of times.
/// </summary>
public sealed class GmailIngestionService(
    IServiceScopeFactory scopes,
    IGmailClientFactory gmailFactory,
    IAIProviderFactory aiFactory,
    SettingsService settings,
    RuntimeStatus runtime,
    TimeProvider clock,
    ILogger<GmailIngestionService> log)
{
    public const int MaxAttempts = 3;
    private static readonly UserContext GmailUser = new("gmail", "Gmail", "server");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DateTime? LastPollUtc { get; private set; }
    public string? LastError { get; private set; }

    public async Task<GmailPollResultDto> PollAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return new GmailPollResultDto(0, 0, 0, 0, "A poll is already running.");
        try
        {
            var result = await PollCoreAsync(ct);
            LastPollUtc = clock.GetUtcNow().UtcDateTime;
            LastError = result.Error;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GmailPollResultDto> PollCoreAsync(CancellationToken ct)
    {
        var gmail = gmailFactory.Create();
        if (gmail is null) return new GmailPollResultDto(0, 0, 0, 0, "Gmail is not connected.");

        IReadOnlyList<string> ids;
        try
        {
            ids = await gmail.ListMessageIdsAsync(settings.Gmail.Query, 50, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Gmail list failed: {Message}", ex.Message);
            return new GmailPollResultDto(0, 0, 0, 0, "Gmail unavailable: " + ex.Message);
        }

        var added = 0;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
            var known = await db.EmailMessages.Where(e => ids.Contains(e.GmailMessageId)).Select(e => e.GmailMessageId).ToListAsync(ct);
            foreach (var id in ids.Except(known))
            {
                db.EmailMessages.Add(new EmailMessage
                {
                    GmailMessageId = id,
                    Status = EmailProcessingStatus.Unprocessed,
                    CreatedUtc = clock.GetUtcNow().UtcDateTime,
                    ReceivedUtc = clock.GetUtcNow().UtcDateTime,
                });
                try
                {
                    await db.SaveChangesAsync(ct);
                    added++;
                }
                catch (DbUpdateException)
                {
                    // Another poll recorded it first (unique Gmail message id): never processed twice.
                    db.ChangeTracker.Clear();
                }
            }
        }

        var processed = 0;
        var failed = 0;
        foreach (var emailId in await PendingIdsAsync(ct))
        {
            var ok = await ProcessAsync(gmail, emailId, ct);
            if (ok is true) processed++;
            else if (ok is false) failed++;
        }
        return new GmailPollResultDto(ids.Count, added, processed, failed, null);
    }

    private async Task<List<int>> PendingIdsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var staleProcessing = clock.GetUtcNow().UtcDateTime.AddMinutes(-15);
        return await db.EmailMessages
            .Where(e => e.Status == EmailProcessingStatus.Unprocessed
                        || (e.Status == EmailProcessingStatus.Failed && e.Attempts < MaxAttempts)
                        || (e.Status == EmailProcessingStatus.Processing && e.UpdatedUtc < staleProcessing))
            .OrderBy(e => e.Id).Select(e => e.Id).Take(20).ToListAsync(ct);
    }

    /// <summary>true = processed, false = failed, null = waiting (AI not configured).</summary>
    private async Task<bool?> ProcessAsync(IGmailClient gmail, int emailId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var email = await db.EmailMessages.FirstAsync(e => e.Id == emailId, ct);

        if (aiFactory.Create() is null)
        {
            email.ProcessingResult = "Waiting: the AI assistant is not configured.";
            email.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return null;
        }

        email.Status = EmailProcessingStatus.Processing;
        email.Attempts++;
        email.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        try
        {
            if (string.IsNullOrEmpty(email.Subject) && string.IsNullOrEmpty(email.BodyText))
            {
                var m = await gmail.GetMessageAsync(email.GmailMessageId, ct);
                email.ThreadId = m.ThreadId;
                email.From = string.IsNullOrEmpty(m.FromName) ? m.FromAddress : $"{m.FromName} <{m.FromAddress}>";
                email.Subject = m.Subject;
                email.ReceivedUtc = m.ReceivedUtc;
                email.BodyText = m.BodyText.Length > 100_000 ? m.BodyText[..100_000] : m.BodyText;
                await db.SaveChangesAsync(ct);
            }

            var ai = scope.ServiceProvider.GetRequiredService<AiService>();
            // Idempotency: if a previous attempt already analysed this e-mail, reuse that result.
            var existing = await db.AiRequests.AsNoTracking().Where(r => r.EmailMessageId == email.Id && r.Status != AiRequestStatus.Failed)
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            AiRequestDto result;
            if (existing is not null)
            {
                result = await ai.GetAsync(existing.Id, ct);
            }
            else
            {
                var (address, name) = GmailParsing.ParseFrom(email.From);
                result = await ai.AnalyzeAsync(new AnalyzeInput(email.BodyText, address, name, email.Subject, email.ReceivedUtc),
                    QuotationSource.Gmail, GmailUser, email.Id, ct);
            }

            email.AiRequestId = result.Id;
            email.QuotationId = result.QuotationId;
            email.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            (email.Status, email.ProcessingResult) = result.Status switch
            {
                AiRequestStatus.Failed => (EmailProcessingStatus.Failed, result.Error ?? "AI analysis failed."),
                AiRequestStatus.NotAQuotation => (EmailProcessingStatus.Ignored, "Not a quotation request."),
                AiRequestStatus.DraftCreated => (EmailProcessingStatus.DraftCreated, "Draft created for review."),
                _ => (EmailProcessingStatus.Completed, "Analysed — needs review in the AI Inbox."),
            };
            email.LastError = result.Status == AiRequestStatus.Failed ? result.Error : null;
            await db.SaveChangesAsync(ct);
            return result.Status != AiRequestStatus.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Processing e-mail {Id} failed: {Message}", email.GmailMessageId, ex.Message);
            email.Status = EmailProcessingStatus.Failed;
            email.LastError = ex.Message;
            email.ProcessingResult = email.Attempts >= MaxAttempts ? "Failed — retries exhausted." : "Failed — will retry.";
            email.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }
    }
}

public sealed class GmailPollingWorker(GmailIngestionService ingestion, SettingsService settings, IOptions<ServerOptions> options,
    ILogger<GmailPollingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (settings.Gmail.Enabled) await ingestion.PollAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Gmail poll failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, settings.Gmail.PollMinutes)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
