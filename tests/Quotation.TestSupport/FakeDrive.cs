using System.Collections.Concurrent;
using Quotation.Gmail;

namespace Quotation.TestSupport;

/// <summary>An office Google Drive kept in memory: folders and the files put there.</summary>
public sealed class FakeDrive : IDriveClient
{
    public sealed record Item(string Id, string Name, string? Parent, bool IsFolder, byte[] Content);

    public ConcurrentDictionary<string, Item> Items { get; } = new();
    public int Uploads;
    /// <summary>Set to make every call fail (e.g. Drive not allowed).</summary>
    public Exception? FailWith { get; set; }

    public Task<string> FolderAsync(string name, string? parentId, CancellationToken ct)
    {
        if (FailWith is not null) throw FailWith;
        var found = Items.Values.FirstOrDefault(i => i.IsFolder && i.Name == name && i.Parent == parentId);
        if (found is not null) return Task.FromResult(found.Id);
        var id = "f" + Items.Count;
        Items[id] = new Item(id, name, parentId, true, []);
        return Task.FromResult(id);
    }

    public async Task<string> UploadAsync(string folderId, string name, string localPath, string mimeType, string? existingId, CancellationToken ct)
    {
        if (FailWith is not null) throw FailWith;
        var content = await File.ReadAllBytesAsync(localPath, ct);
        var id = existingId ?? "u" + Items.Count;
        Items[id] = new Item(id, name, folderId, false, content);
        Interlocked.Increment(ref Uploads);
        return id;
    }

    /// <summary>"TS Quotations/Approved PDFs/2026-11/TSQ2627-1 - Buyer.pdf" for each file.</summary>
    public List<string> Paths() => Items.Values.Where(i => !i.IsFolder).Select(PathOf).OrderBy(p => p, StringComparer.Ordinal).ToList();

    private string PathOf(Item i) => i.Parent is { } p && Items.TryGetValue(p, out var parent) ? PathOf(parent) + "/" + i.Name : i.Name;
}
