using System.Collections.Concurrent;

namespace Quotation.Server.Services;

/// <summary>
/// Who has which quotation open right now (in memory only; nothing is stored). Each open quotation screen checks in
/// every 15 seconds; someone not seen for 45 seconds no longer counts. Used for "Rasik (PC) also has this open".
/// </summary>
public sealed class PresenceTracker(TimeProvider clock)
{
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(45);

    private sealed record Seen(string User, string Machine, DateTime AtUtc);

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, Seen>> _open = new();

    /// <summary>Records that this user/PC has the quotation open and returns everyone else who does.</summary>
    public IReadOnlyList<Contracts.PresenceDto> CheckIn(Guid quotationId, string userName, string displayName, string machine)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var viewers = _open.GetOrAdd(quotationId, _ => new ConcurrentDictionary<string, Seen>(StringComparer.OrdinalIgnoreCase));
        viewers[Key(userName, machine)] = new Seen(displayName, machine, now);
        foreach (var (key, seen) in viewers)
            if (now - seen.AtUtc > Expiry) viewers.TryRemove(key, out _);
        return viewers.Where(v => v.Key != Key(userName, machine))
            .Select(v => new Contracts.PresenceDto(v.Value.User, v.Value.Machine))
            .OrderBy(p => p.User, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Leave(Guid quotationId, string userName, string machine)
    {
        if (!_open.TryGetValue(quotationId, out var viewers)) return;
        viewers.TryRemove(Key(userName, machine), out _);
        if (viewers.IsEmpty) _open.TryRemove(quotationId, out _);
    }

    private static string Key(string user, string machine) => user + "@" + machine;
}
