using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.AI;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;

namespace Quotation.Server.Services;

/// <summary>Creates the configured AI provider. Replaceable in tests.</summary>
public interface IAIProviderFactory
{
    /// <summary>Returns null when AI is disabled or no API key is configured.</summary>
    IAIProvider? Create();
}

public sealed class ClaudeProviderFactory(SettingsService settings, ILoggerFactory loggers) : IAIProviderFactory
{
    public IAIProvider? Create()
    {
        var ai = settings.Ai;
        if (!ai.Enabled) return null;
        var key = settings.GetSecret(SettingsService.SecretAiApiKey);
        if (string.IsNullOrWhiteSpace(key)) return null;
        return new ClaudeProvider(new ClaudeOptions(key, string.IsNullOrWhiteSpace(ai.Model) ? "claude-opus-5-5" : ai.Model.Trim()),
            loggers.CreateLogger<ClaudeProvider>());
    }
}

/// <summary>
/// Runs AI analyses (typed requests and e-mails), stores them for the AI Inbox, and turns them into
/// quotation drafts. Drafts created by AI are always PENDING_REVIEW and un-numbered until a person saves
/// or approves them.
/// </summary>
public sealed class AiService(
    QuotationDbContext db,
    IAIProviderFactory providers,
    AiDataTools dataTools,
    QuotationService quotations,
    SettingsService settings,
    AuditService audit,
    FinancialYearService fy,
    TimeProvider clock,
    ILogger<AiService> log)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => providers.Create() is not null;

    /// <summary>Analyzes the text; if everything is unambiguous a review draft is created immediately.</summary>
    public async Task<AiRequestDto> AnalyzeAsync(AnalyzeInput input, QuotationSource source, UserContext user, int? emailMessageId,
        CancellationToken ct)
    {
        var provider = providers.Create()
                       ?? throw new QuotationException(503, "AI_NOT_CONFIGURED", "The AI assistant is not configured. An administrator can enable it in Settings → Claude AI.");
        var now = clock.GetUtcNow().UtcDateTime;
        var record = new AiRequest
        {
            Source = source,
            EmailMessageId = emailMessageId,
            InputText = input.Text.Length > 100_000 ? input.Text[..100_000] : input.Text,
            Subject = input.Subject ?? "",
            FromAddress = input.SenderEmail ?? "",
            ReceivedUtc = input.ReceivedUtc,
            Status = AiRequestStatus.Processing,
            CreatedBy = user.UserName,
            CreatedUtc = now,
        };
        db.AiRequests.Add(record);
        await db.SaveChangesAsync(ct);

        try
        {
            var assistant = new QuotationAssistant(provider, dataTools) { MaxCandidates = Math.Clamp(settings.Ai.MaxCandidates, 3, 25) };
            var analysis = await assistant.AnalyzeAsync(input, ct);
            record.AnalysisJson = JsonSerializer.Serialize(analysis, Json);
            record.Summary = analysis.Summary.Length > 500 ? analysis.Summary[..500] : analysis.Summary;
            record.Model = analysis.Model;
            record.InputTokens = analysis.InputTokens;
            record.OutputTokens = analysis.OutputTokens;
            record.Status = analysis.Status switch
            {
                AnalysisStatus.ReadyForReview => AiRequestStatus.ReadyForReview,
                AnalysisStatus.NotAQuotationRequest => AiRequestStatus.NotAQuotation,
                _ => AiRequestStatus.NeedsClarification,
            };
            record.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            audit.Add(user.UserName, user.Machine, "AiAnalysis", "AiRequest", record.Id.ToString(),
                $"{record.Status}; model={record.Model}; tokens={record.InputTokens}/{record.OutputTokens}");
            await db.SaveChangesAsync(ct);

            if (analysis.Status == AnalysisStatus.ReadyForReview)
            {
                await CreateDraftAsync(record.Id, new CreateDraftFromAiRequest
                {
                    CustomerId = analysis.Customer.CustomerId!.Value,
                    Lines = analysis.Lines.Select(l => new AiDraftLine { ProductId = l.ProductId!.Value, Quantity = l.Quantity!.Value }).ToList(),
                }, user, ct);
            }
        }
        catch (AiUnavailableException ex)
        {
            record.Status = AiRequestStatus.Failed;
            record.Error = ex.Message;
            record.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(CancellationToken.None);
            log.LogWarning("AI analysis {Id} failed: {Message}", record.Id, ex.Message);
        }
        return await GetAsync(record.Id, ct);
    }

    /// <summary>Creates a PENDING_REVIEW draft from a (possibly user-corrected) analysis.</summary>
    public async Task<AiRequestDto> CreateDraftAsync(int requestId, CreateDraftFromAiRequest decisions, UserContext user, CancellationToken ct)
    {
        var record = await db.AiRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                     ?? throw new QuotationException(404, "NOT_FOUND", "AI request not found.");
        if (record.QuotationId is not null) throw new QuotationException(409, ApiErrorCodes.InvalidState, "A draft was already created for this request.");
        if (decisions.Lines.Count == 0) throw new QuotationException(400, ApiErrorCodes.Validation, "Select at least one product.");
        if (decisions.Lines.Any(l => l.Quantity <= 0)) throw new QuotationException(400, ApiErrorCodes.Validation, "Every line needs a quantity greater than zero.");
        if (!await db.Customers.AnyAsync(c => c.Id == decisions.CustomerId && !c.IsDeleted, ct))
            throw new QuotationException(400, ApiErrorCodes.Validation, "The selected customer does not exist in Tally.");

        var analysis = string.IsNullOrEmpty(record.AnalysisJson) ? null : JsonSerializer.Deserialize<QuotationAnalysis>(record.AnalysisJson, Json);
        var ids = decisions.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && !p.IsDeleted).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Distinct().Count()) throw new QuotationException(400, ApiErrorCodes.Validation, "A selected product does not exist in Tally.");

        var company = settings.Company;
        var request = new SaveQuotationRequest
        {
            Date = fy.Today(),
            CustomerId = decisions.CustomerId,
            BuyersReference = analysis?.Reference ?? "",
            BuyersReferenceDate = record.ReceivedUtc is { } r ? DateOnly.FromDateTime(r) : null,
            OtherReferences = record.Source == QuotationSource.Gmail && record.Subject.Length > 0 ? $"Your e-mail: {record.Subject}" : "",
            PaymentTerms = company.DefaultPaymentTerms,
            TermsOfDelivery = company.DefaultTermsOfDelivery,
            TermsAndConditions = string.Join("\n", company.TermsAndConditions),
            PreparedBy = company.DefaultPreparedBy,
            VerifiedBy = company.DefaultVerifiedBy,
            PackingForwarding = settings.Quotation.DefaultPackingForwardingAmount,
            PackingForwardingPercent = settings.Quotation.DefaultPackingForwardingPercent > 0 ? settings.Quotation.DefaultPackingForwardingPercent : null,
            Lines = decisions.Lines.Select(l => new QuotationLineDto
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                Rate = -1, // use the current Tally rate
                Description = l.Description ?? products[l.ProductId].Description,
            }).ToList(),
        };
        var source = record.Source == QuotationSource.Gmail ? QuotationSource.Gmail : QuotationSource.Ai;
        var dto = await quotations.CreateAsync(request, source, user, ct, assignNumber: false, sourceEmailId: record.EmailMessageId);

        record.QuotationId = dto.Id;
        record.Status = AiRequestStatus.DraftCreated;
        record.UpdatedBy = user.UserName;
        record.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        audit.Add(user.UserName, user.Machine, "AiDraftCreated", "AiRequest", record.Id.ToString(), $"Quotation {dto.Id}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(record.Id, ct);
    }

    public async Task<AiRequestDto> DismissAsync(int requestId, UserContext user, CancellationToken ct)
    {
        var record = await db.AiRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                     ?? throw new QuotationException(404, "NOT_FOUND", "AI request not found.");
        record.Status = AiRequestStatus.Dismissed;
        record.UpdatedBy = user.UserName;
        record.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        audit.Add(user.UserName, user.Machine, "AiRequestDismissed", "AiRequest", record.Id.ToString());
        await db.SaveChangesAsync(ct);
        return await GetAsync(requestId, ct);
    }

    public async Task<AiRequestDto> GetAsync(int id, CancellationToken ct)
    {
        var r = await db.AiRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new QuotationException(404, "NOT_FOUND", "AI request not found.");
        return (await ToDtosAsync([r], ct))[0];
    }

    public async Task<List<AiRequestDto>> ListAsync(AiRequestStatus? status, int take, CancellationToken ct)
    {
        var q = db.AiRequests.AsNoTracking().AsQueryable();
        if (status is not null) q = q.Where(r => r.Status == status);
        var rows = await q.OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    private async Task<List<AiRequestDto>> ToDtosAsync(List<AiRequest> rows, CancellationToken ct)
    {
        var qids = rows.Where(r => r.QuotationId is not null).Select(r => r.QuotationId!.Value).ToList();
        var numbers = await db.Quotations.AsNoTracking().Where(q => qids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, q => q.Number, ct);
        return rows.Select(r => new AiRequestDto
        {
            Id = r.Id,
            Source = r.Source,
            Status = r.Status,
            InputText = r.InputText,
            Subject = r.Subject,
            FromAddress = r.FromAddress,
            ReceivedUtc = r.ReceivedUtc,
            Summary = r.Summary,
            Analysis = string.IsNullOrEmpty(r.AnalysisJson) ? null : JsonSerializer.Deserialize<QuotationAnalysis>(r.AnalysisJson, Json),
            QuotationId = r.QuotationId,
            QuotationNumber = r.QuotationId is { } q ? numbers.GetValueOrDefault(q) : null,
            Error = r.Error,
            CreatedBy = r.CreatedBy,
            CreatedUtc = r.CreatedUtc,
        }).ToList();
    }
}
