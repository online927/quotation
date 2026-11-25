using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Search;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Round 38 (PL-1): product web pages loaded by the administrator from an Excel (product code → link). Kept on the
/// products in the app's own database (never written to Tally) and in memory for fast look-ups.
/// </summary>
public sealed class ProductLinkService(IServiceScopeFactory scopes)
{
    private readonly ConcurrentDictionary<int, string> _links = new();
    private volatile bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>The link of a product ("" = none).</summary>
    public string For(int? productId)
    {
        if (productId is not { } id) return "";
        EnsureLoaded();
        return _links.TryGetValue(id, out var link) ? link : "";
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _lock.Wait();
        try
        {
            if (_loaded) return;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
            foreach (var p in db.Products.AsNoTracking().Where(p => p.Link != "").Select(p => new { p.Id, p.Link }).ToList())
                _links[p.Id] = p.Link;
            _loaded = true;
        }
        finally { _lock.Release(); }
    }

    /// <summary>A web link worth keeping: http or https only.</summary>
    public static string? CleanUrl(string? url)
    {
        var u = (url ?? "").Trim();
        if (u.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) u = "https://" + u;
        return Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri : null;
    }

    /// <summary>
    /// Saves the links of the rows: each code is matched to a product by its Part No., then its alias / code at the start of
    /// the item name, then the exact item name (letters and digits compared, so "187-901-10" = "18790110").
    /// </summary>
    public async Task<ProductLinksResultDto> ImportAsync(IReadOnlyList<ProductLinkRowDto> rows, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var products = await db.Products.Where(p => !p.IsDeleted).ToListAsync(ct);
        var byPart = new Dictionary<string, Data.Entities.Product>();
        var byAlias = new Dictionary<string, Data.Entities.Product>();
        var byName = new Dictionary<string, Data.Entities.Product>();
        foreach (var p in products)
        {
            var part = TextNormalizer.Compact(p.PartNumber);
            if (part.Length > 0) byPart.TryAdd(part, p);
            foreach (var a in p.Aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var c = TextNormalizer.Compact(a);
                if (c.Length > 0) byAlias.TryAdd(c, p);
            }
            // The code at the start of the item name ("187-901-10-UNIVERSAL BEVEL PROTRACTOR" → "187-901-10").
            var lead = p.Name.Split(' ', 2)[0];
            foreach (var code in new[] { lead, LeadingCode(p.Name) })
            {
                var c = TextNormalizer.Compact(code);
                if (c.Length >= 4 && c.Any(char.IsDigit)) byAlias.TryAdd(c, p);
            }
            var n = TextNormalizer.Compact(p.Name);
            if (n.Length > 0) byName.TryAdd(n, p);
        }

        var notFound = new List<string>();
        var bad = new List<string>();
        var linked = 0;
        var count = 0;
        foreach (var row in rows)
        {
            var code = (row.Code ?? "").Trim();
            if (code.Length == 0 && string.IsNullOrWhiteSpace(row.Url)) continue;
            count++;
            var url = CleanUrl(row.Url);
            if (url is null)
            {
                bad.Add(code.Length > 0 ? code : row.Url);
                continue;
            }
            var key = TextNormalizer.Compact(code);
            if (key.Length == 0 || !(byPart.TryGetValue(key, out var product) || byAlias.TryGetValue(key, out product) || byName.TryGetValue(key, out product)))
            {
                notFound.Add(code);
                continue;
            }
            if (product.Link != url) product.Link = url;
            _links[product.Id] = url;
            linked++;
        }
        await db.SaveChangesAsync(ct);
        return new ProductLinksResultDto(count, linked, notFound, bad);
    }

    /// <summary>"187-901-10-UNIVERSAL BEVEL" → "187-901-10" (the digits-and-dashes code before the first word).</summary>
    private static string LeadingCode(string name)
    {
        var parts = name.Split('-');
        var code = new List<string>();
        foreach (var p in parts)
        {
            if (p.Length == 0 || !p.All(char.IsLetterOrDigit) || !p.Any(char.IsDigit)) break;
            code.Add(p);
        }
        return string.Join("-", code);
    }
}
