using Quotation.Gmail;

namespace Quotation.TestSupport;

/// <summary>In-memory mailbox implementing the Gmail client interface.</summary>
public sealed class FakeGmail : IGmailClient
{
    public List<GmailMessageData> Messages { get; } = [];
    public int GetCount { get; private set; }
    public bool Offline { get; set; }

    public GmailMessageData Add(string from, string fromName, string subject, string body)
    {
        var m = new GmailMessageData($"msg{Messages.Count + 1:000}", $"thr{Messages.Count + 1:000}", from, fromName, subject,
            DateTime.UtcNow.AddMinutes(-Messages.Count), body);
        Messages.Add(m);
        return m;
    }

    public Task<string> GetAccountEmailAsync(CancellationToken ct) => Task.FromResult("sales@tsaifuddin.example");

    public Task<IReadOnlyList<string>> ListMessageIdsAsync(string query, int max, CancellationToken ct)
    {
        if (Offline) throw new HttpRequestException("Network unreachable");
        return Task.FromResult<IReadOnlyList<string>>(Messages.Select(m => m.Id).Take(max).ToList());
    }

    public Task<GmailMessageData> GetMessageAsync(string id, CancellationToken ct)
    {
        GetCount++;
        return Task.FromResult(Messages.Single(m => m.Id == id));
    }
}
