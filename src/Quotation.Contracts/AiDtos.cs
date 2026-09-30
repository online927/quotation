namespace Quotation.Contracts;

/// <summary>Search result evidence produced by the application's search index (not by the AI).</summary>
public sealed record MatchEvidenceInfo(bool AllTermsMatched, bool ExactName, bool ExactIdentifier, bool UsedFuzzy, double Score);

public sealed record ProductCandidate(
    int Id,
    string Name,
    string PartNumber,
    string Brand,
    string Unit,
    string Hsn,
    decimal? GstRate,
    decimal? Rate,
    string Description,
    MatchEvidenceInfo Evidence);

public sealed record CustomerCandidate(
    int Id,
    string Name,
    string StateName,
    string Gstin,
    string Email,
    string City,
    MatchEvidenceInfo Evidence);

public sealed record QuotationHistoryItem(string Number, DateOnly Date, string Customer, string Items, decimal Total);

public enum AnalysisStatus
{
    /// <summary>Customer, products and quantities are all resolved: a draft can be created for review.</summary>
    ReadyForReview,
    /// <summary>Something needs a human decision (customer, product choice or quantity).</summary>
    NeedsClarification,
    /// <summary>Not a quotation request (e.g. newsletter, invoice query).</summary>
    NotAQuotationRequest,
}

public enum ResolutionStatus
{
    Matched,
    NeedsSelection,
    NotFound,
    NeedsQuantity,
}

public sealed class CustomerResolution
{
    public ResolutionStatus Status { get; set; }
    public string Query { get; set; } = "";
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string Reason { get; set; } = "";
    public List<CustomerCandidate> Candidates { get; set; } = [];
}

public sealed class LineResolution
{
    public ResolutionStatus Status { get; set; }
    /// <summary>What the customer asked for, in their words.</summary>
    public string RequestedText { get; set; } = "";
    public string ProductQuery { get; set; } = "";
    public string Specifications { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string RequestedUnit { get; set; } = "";
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string Reason { get; set; } = "";
    public List<ProductCandidate> Candidates { get; set; } = [];
}

/// <summary>The application's validated interpretation of a request (the AI's proposal after checks).</summary>
public sealed class QuotationAnalysis
{
    public AnalysisStatus Status { get; set; }
    public string Summary { get; set; } = "";
    public CustomerResolution Customer { get; set; } = new();
    public List<LineResolution> Lines { get; set; } = [];
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Questions { get; set; } = [];
    /// <summary>The model's own confidence — informational only, never used to accept a product.</summary>
    public double? ModelConfidence { get; set; }
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public List<AiToolCallRecord> ToolCalls { get; set; } = [];
}

public sealed record AiToolCallRecord(string Name, string InputJson, string Output, bool IsError);

public sealed record AnalyzeRequest(string Text);

public sealed class AiRequestDto
{
    public int Id { get; set; }
    public Quotation.Core.Domain.QuotationSource Source { get; set; }
    public Quotation.Core.Domain.AiRequestStatus Status { get; set; }
    public string InputText { get; set; } = "";
    public string Subject { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public DateTime? ReceivedUtc { get; set; }
    public string Summary { get; set; } = "";
    public QuotationAnalysis? Analysis { get; set; }
    public Guid? QuotationId { get; set; }
    public string? QuotationNumber { get; set; }
    public string? Error { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

/// <summary>The user's decisions for an AI request (selected customer/products, quantities).</summary>
public sealed class CreateDraftFromAiRequest
{
    public int CustomerId { get; set; }
    public List<AiDraftLine> Lines { get; set; } = [];
}

public sealed class AiDraftLine
{
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Description { get; set; }
}

public sealed record GmailStatusDto(
    bool Enabled,
    bool ClientConfigured,
    bool Connected,
    string? Account,
    string Query,
    DateTime? LastPollUtc,
    string? LastError,
    IReadOnlyDictionary<string, int> Counts);

public sealed record GmailConnectStartRequest(string RedirectUri);
public sealed record GmailConnectStartResponse(string AuthorizationUrl, string State);
public sealed record GmailConnectCompleteRequest(string Code, string State, string RedirectUri);

public sealed record EmailMessageDto(
    int Id,
    string GmailMessageId,
    string From,
    string Subject,
    DateTime ReceivedUtc,
    Quotation.Core.Domain.EmailProcessingStatus Status,
    string ProcessingResult,
    int Attempts,
    string? LastError,
    int? AiRequestId,
    Guid? QuotationId);

public sealed record GmailPollResultDto(int Found, int New, int Processed, int Failed, string? Error);
