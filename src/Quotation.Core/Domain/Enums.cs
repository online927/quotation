namespace Quotation.Core.Domain;

/// <summary>Lifecycle of a quotation. Only a user can move a quotation to Approved/Generated.</summary>
public enum QuotationStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    Generated = 3,
    Cancelled = 4,
}

/// <summary>Where a quotation came from.</summary>
public enum QuotationSource
{
    Manual = 0,
    Ai = 1,
    Gmail = 2,
}

/// <summary>Processing state of an ingested e-mail.</summary>
public enum EmailProcessingStatus
{
    Unprocessed = 0,
    Processing = 1,
    DraftCreated = 2,
    Completed = 3,
    Failed = 4,
    Ignored = 5,
}

public enum UserRole
{
    User = 0,
    Admin = 1,
}

/// <summary>How GST is presented on the quotation.</summary>
public enum TaxPresentation
{
    /// <summary>Show GST % per line only; totals are exclusive of GST (standard Tally quotation).</summary>
    RateOnly = 0,
    /// <summary>Compute CGST/SGST or IGST and include it in the grand total.</summary>
    ComputeTax = 1,
}

public enum SyncKind
{
    Full = 0,
    Incremental = 1,
}

public enum SyncStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3,
}
