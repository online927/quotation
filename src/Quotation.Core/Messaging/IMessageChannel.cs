namespace Quotation.Core.Messaging;

/// <summary>
/// Extension point for sending generated quotations to customers (future WhatsApp module).
/// DISABLED in V1: no implementation is registered and nothing is ever sent automatically.
/// A future module implements this interface and is invoked only after explicit user action on an
/// approved quotation (human-in-the-loop is kept).
/// </summary>
public interface IMessageChannel
{
    string Name { get; }
    bool IsEnabled { get; }
    Task SendQuotationAsync(OutgoingQuotation message, CancellationToken ct);
}

public sealed record OutgoingQuotation(string QuotationNumber, string RecipientName, string RecipientAddress, string Text, byte[] Pdf, string FileName);

public static class QuotationMessageTemplate
{
    /// <summary>Default covering text, e.g. for WhatsApp.</summary>
    public const string Default = "Dear Sir/Madam,\n\nPlease find attached our quotation {number}.\n\nRegards,\n{company}";

    public static string Render(string template, string number, string company) =>
        template.Replace("{number}", number).Replace("{company}", company);
}
