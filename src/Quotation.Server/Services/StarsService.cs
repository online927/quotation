using Quotation.Contracts;

namespace Quotation.Server.Services;

/// <summary>
/// The items and customers each user starred (kept in the settings store, one entry per user). Starred ones come first in
/// that user's searches while the administrator's Stars switch is on. Never touches Tally.
/// </summary>
public sealed class StarsService(SettingsService settings)
{
    public const int MaxStars = 500;

    private sealed class UserStars
    {
        public List<int> Products { get; set; } = [];
        public List<int> Customers { get; set; } = [];
    }

    private static string Key(string user) => "stars:" + user.Trim().ToLowerInvariant();

    public bool Enabled => settings.Quotation.Stars;

    public StarsDto Get(string user)
    {
        var s = settings.Get<UserStars>(Key(user));
        return new StarsDto(s.Products, s.Customers, Enabled);
    }

    public HashSet<int> Products(string user) => Enabled ? [.. settings.Get<UserStars>(Key(user)).Products] : [];
    public HashSet<int> Customers(string user) => Enabled ? [.. settings.Get<UserStars>(Key(user)).Customers] : [];

    public async Task<StarsDto> SetAsync(string user, StarRequest req, CancellationToken ct)
    {
        var s = settings.Get<UserStars>(Key(user));
        var list = req.Kind.Equals("customer", StringComparison.OrdinalIgnoreCase) ? s.Customers : s.Products;
        list.Remove(req.Id);
        if (req.On) list.Insert(0, req.Id);
        if (list.Count > MaxStars) list.RemoveRange(MaxStars, list.Count - MaxStars);
        await settings.SaveAsync(Key(user), s, user, ct);
        return new StarsDto(s.Products, s.Customers, Enabled);
    }

    /// <summary>Starred first, otherwise in the order found (a stable sort).</summary>
    public static List<T> StarredFirst<T>(IEnumerable<T> hits, Func<T, int> id, HashSet<int> starred) =>
        starred.Count == 0 ? hits.ToList() : hits.OrderBy(h => starred.Contains(id(h)) ? 0 : 1).ToList();
}
