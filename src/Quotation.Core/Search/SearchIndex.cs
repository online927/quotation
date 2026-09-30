namespace Quotation.Core.Search;

/// <summary>How a field participates in search.</summary>
public enum FieldKind
{
    /// <summary>Free text (names, descriptions): tokenized.</summary>
    Text,
    /// <summary>Identifier (part number, GSTIN, phone): also matched in compact form without separators.</summary>
    Identifier,
}

public sealed record SearchField<T>(string Name, Func<T, string?> Get, double Weight, FieldKind Kind = FieldKind.Text, bool IsPrimaryName = false);

public enum TokenMatchKind
{
    None = 0,
    Fuzzy = 1,
    Contains = 2,
    Prefix = 3,
    Exact = 4,
}

/// <summary>Why a document matched — used for ranking explanations and AI safety decisions.</summary>
public sealed record MatchEvidence(
    bool AllTokensMatched,
    double Coverage,
    bool ExactName,
    bool NameStartsWithQuery,
    bool ExactIdentifier,
    bool IdentifierPrefix,
    bool UsedFuzzy,
    IReadOnlyList<string> MatchedFields);

public sealed record SearchHit<T>(T Item, double Score, MatchEvidence Evidence);

/// <summary>
/// In-memory inverted index with prefix, substring (trigram) and typo-tolerant (Damerau–Levenshtein)
/// matching and field-weighted scoring. Immutable after construction; rebuild and swap to update.
/// Designed for 10–50k documents with single-digit-millisecond queries.
/// </summary>
public sealed class SearchIndex<T>
{
    private readonly SearchField<T>[] _fields;
    private readonly T[] _items;
    private readonly string[] _normalizedName;
    private readonly string[][] _compactIds;      // per doc: compact identifiers (part no., name)
    private readonly string[] _tokens;             // tokenId → token
    private readonly Dictionary<string, int> _tokenIds = new(StringComparer.Ordinal);
    private readonly List<(int Doc, int Field)>[] _postings;
    private readonly int[] _sortedTokenIds;        // tokenIds sorted by token text (prefix search)
    private readonly Dictionary<string, List<int>> _trigrams = new(StringComparer.Ordinal); // trigram → tokenIds

    public int Count => _items.Length;

    public SearchIndex(IEnumerable<T> items, IReadOnlyList<SearchField<T>> fields)
    {
        _fields = fields.ToArray();
        _items = items.ToArray();
        _normalizedName = new string[_items.Length];
        _compactIds = new string[_items.Length][];
        var postings = new List<List<(int, int)>>();
        var tokens = new List<string>();

        for (var d = 0; d < _items.Length; d++)
        {
            var compacts = new List<string>();
            for (var f = 0; f < _fields.Length; f++)
            {
                var field = _fields[f];
                var value = field.Get(_items[d]);
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (field.IsPrimaryName && _normalizedName[d] is null)
                {
                    _normalizedName[d] = string.Join(' ', TextNormalizer.QueryTokens(value));
                    compacts.Add(TextNormalizer.Compact(value));
                }
                if (field.Kind == FieldKind.Identifier)
                {
                    foreach (var line in value.Split('\n'))
                    {
                        var c = TextNormalizer.Compact(line);
                        if (c.Length >= 2) compacts.Add(c);
                    }
                }
                foreach (var token in TextNormalizer.IndexTokens(value).Distinct())
                {
                    if (!_tokenIds.TryGetValue(token, out var id))
                    {
                        id = tokens.Count;
                        tokens.Add(token);
                        _tokenIds[token] = id;
                        postings.Add([]);
                    }
                    postings[id].Add((d, f));
                }
            }
            _normalizedName[d] ??= "";
            _compactIds[d] = compacts.Distinct().ToArray();
        }

        _tokens = tokens.ToArray();
        _postings = postings.ToArray();
        _sortedTokenIds = Enumerable.Range(0, _tokens.Length).OrderBy(i => _tokens[i], StringComparer.Ordinal).ToArray();
        for (var id = 0; id < _tokens.Length; id++)
        {
            foreach (var tri in Trigrams(_tokens[id]))
            {
                if (!_trigrams.TryGetValue(tri, out var list)) _trigrams[tri] = list = [];
                list.Add(id);
            }
        }
    }

    public IReadOnlyList<SearchHit<T>> Search(string? query, int limit = 20, Func<T, bool>? filter = null)
    {
        var queryTokens = TextNormalizer.QueryTokens(query);
        if (queryTokens.Count == 0 || _items.Length == 0) return [];
        var queryCompact = TextNormalizer.Compact(query);
        var queryNormalized = string.Join(' ', queryTokens);

        // Per document: best score for each query token and which fields matched.
        var perDoc = new Dictionary<int, DocState>();
        for (var q = 0; q < queryTokens.Count; q++)
        {
            foreach (var (tokenId, kind, quality) in MatchTokens(queryTokens[q]))
            {
                foreach (var (doc, field) in _postings[tokenId])
                {
                    if (!perDoc.TryGetValue(doc, out var state))
                    {
                        state = new DocState(queryTokens.Count);
                        perDoc[doc] = state;
                    }
                    var score = BaseScore(kind) * quality * _fields[field].Weight;
                    if (score > state.TokenScores[q])
                    {
                        state.TokenScores[q] = score;
                        state.TokenKinds[q] = kind;
                    }
                    state.Fields.Add(field);
                }
            }
        }

        // Compact identifier matches ("18790110", "187-901") even when tokenization differs.
        if (queryCompact.Length >= 4 && queryCompact.Any(char.IsDigit))
        {
            for (var d = 0; d < _items.Length; d++)
            {
                foreach (var c in _compactIds[d])
                {
                    if (!c.Contains(queryCompact, StringComparison.Ordinal)) continue;
                    if (!perDoc.TryGetValue(d, out var state))
                    {
                        state = new DocState(queryTokens.Count);
                        perDoc[d] = state;
                    }
                    state.CompactExact |= c == queryCompact;
                    state.CompactPrefix |= c.StartsWith(queryCompact, StringComparison.Ordinal);
                    state.CompactContains = true;
                }
            }
        }

        var hits = new List<SearchHit<T>>(Math.Min(perDoc.Count, 512));
        foreach (var (doc, state) in perDoc)
        {
            var item = _items[doc];
            if (filter is not null && !filter(item)) continue;

            var matched = 0;
            var score = 0.0;
            var fuzzy = false;
            for (var q = 0; q < queryTokens.Count; q++)
            {
                if (state.TokenKinds[q] == TokenMatchKind.None) continue;
                matched++;
                score += state.TokenScores[q];
                fuzzy |= state.TokenKinds[q] == TokenMatchKind.Fuzzy;
            }
            if (state.CompactContains && matched < queryTokens.Count)
            {
                // The identifier match explains the whole query (e.g. "187901" or "18790110").
                matched = queryTokens.Count;
            }
            var coverage = (double)matched / queryTokens.Count;

            var name = _normalizedName[doc];
            var exactName = name.Length > 0 && name == queryNormalized;
            var startsWith = name.Length > 0 && name.StartsWith(queryNormalized, StringComparison.Ordinal);
            if (exactName) score += 100;
            else if (startsWith) score += 30;
            if (state.CompactExact) score += 80;
            else if (state.CompactPrefix) score += 45;
            else if (state.CompactContains) score += 15;
            if (coverage >= 1) score += 25;
            score -= name.Length * 0.01; // prefer shorter, more specific names on ties

            var fields = state.Fields.Select(f => _fields[f].Name).Distinct().ToList();
            if (state.CompactContains) fields.Add("Identifier");
            hits.Add(new SearchHit<T>(item, score, new MatchEvidence(
                coverage >= 1, coverage, exactName, startsWith, state.CompactExact, state.CompactPrefix, fuzzy, fields)));
        }

        // Prefer documents matching every query term; fall back to partial matches only when none do.
        var complete = hits.Where(h => h.Evidence.AllTokensMatched).ToList();
        var pool = complete.Count > 0 ? complete : hits.Where(h => h.Evidence.Coverage >= 0.5).ToList();
        return pool.OrderByDescending(h => h.Score).Take(limit).ToList();
    }

    // ---------- token matching ----------

    private IEnumerable<(int TokenId, TokenMatchKind Kind, double Quality)> MatchTokens(string q)
    {
        var seen = new HashSet<int>();
        if (_tokenIds.TryGetValue(q, out var exact))
        {
            seen.Add(exact);
            yield return (exact, TokenMatchKind.Exact, 1.0);
        }

        // Prefix: binary search in the sorted token list.
        var lo = LowerBound(q);
        var prefixCount = 0;
        for (var i = lo; i < _sortedTokenIds.Length; i++)
        {
            var id = _sortedTokenIds[i];
            var t = _tokens[id];
            if (!t.StartsWith(q, StringComparison.Ordinal)) break;
            if (!seen.Add(id)) continue;
            prefixCount++;
            yield return (id, TokenMatchKind.Prefix, 0.7 + 0.3 * q.Length / t.Length);
        }

        if (q.Length < 3) yield break;

        // Contains: tokens holding all trigrams of the query token, verified with Contains().
        var candidates = CandidatesByTrigrams(q, requireAll: true);
        foreach (var id in candidates)
        {
            if (seen.Contains(id)) continue;
            var at = _tokens[id].IndexOf(q, StringComparison.Ordinal);
            // "10mm" must not match inside "110mm" or "2.10mm": numbers only match at a number boundary.
            if (at > 0 && char.IsDigit(q[0]) && (char.IsDigit(_tokens[id][at - 1]) || _tokens[id][at - 1] == '.')) at = -1;
            if (at >= 0)
            {
                seen.Add(id);
                yield return (id, TokenMatchKind.Contains, 0.6 + 0.4 * q.Length / _tokens[id].Length);
            }
        }

        // Fuzzy (typos): only for alphabetic-ish tokens of 4+ chars; numbers must match exactly.
        if (q.Length < 4 || TextNormalizer.IsNumber(q)) yield break;
        var maxEdits = q.Length <= 5 ? 1 : 2;
        var queryDigits = Digits(q);
        foreach (var id in CandidatesByTrigrams(q, requireAll: false))
        {
            if (seen.Contains(id)) continue;
            var t = _tokens[id];
            // Typos are tolerated in words, never in numbers: "2.5sqmm" must not match "1.5sqmm".
            if (queryDigits.Length > 0 && !Digits(t).StartsWith(queryDigits, StringComparison.Ordinal)) continue;
            var distance = DamerauLevenshtein(q, t, maxEdits);
            var quality = distance == 1 ? 0.8 : 0.55;
            if (distance > maxEdits && t.Length > q.Length)
            {
                // A typo while still typing: compare with the start of the longer token.
                var prefixDistance = Math.Min(
                    DamerauLevenshtein(q, t[..q.Length], maxEdits),
                    DamerauLevenshtein(q, t[..Math.Min(t.Length, q.Length + 1)], maxEdits));
                if (prefixDistance <= maxEdits)
                {
                    distance = prefixDistance;
                    quality = 0.5;
                }
            }
            if (distance <= maxEdits)
            {
                seen.Add(id);
                yield return (id, TokenMatchKind.Fuzzy, quality);
            }
        }
    }

    private static string Digits(string s) => new(s.Where(c => char.IsDigit(c) || c == '.').ToArray());

    private IEnumerable<int> CandidatesByTrigrams(string q, bool requireAll)
    {
        var grams = Trigrams(q).Distinct().ToList();
        if (grams.Count == 0) return [];
        var counts = new Dictionary<int, int>();
        foreach (var g in grams)
        {
            if (!_trigrams.TryGetValue(g, out var ids)) continue;
            foreach (var id in ids) counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        var needed = requireAll ? grams.Count : Math.Max(1, grams.Count - 2 * (q.Length <= 5 ? 1 : 2) - 1);
        return counts.Where(kv => kv.Value >= needed).Select(kv => kv.Key);
    }

    private static IEnumerable<string> Trigrams(string token)
    {
        if (token.Length < 3) yield break;
        for (var i = 0; i + 3 <= token.Length; i++) yield return token.Substring(i, 3);
    }

    private int LowerBound(string prefix)
    {
        int lo = 0, hi = _sortedTokenIds.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (string.CompareOrdinal(_tokens[_sortedTokenIds[mid]], prefix) < 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static double BaseScore(TokenMatchKind kind) => kind switch
    {
        TokenMatchKind.Exact => 10,
        TokenMatchKind.Prefix => 7,
        TokenMatchKind.Contains => 4,
        TokenMatchKind.Fuzzy => 3.5,
        _ => 0,
    };

    /// <summary>Optimal-string-alignment distance with early exit above <paramref name="max"/>.</summary>
    public static int DamerauLevenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var rowMin = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > max) return max + 1;
        }
        return d[a.Length, b.Length];
    }

    private sealed class DocState(int queryTokenCount)
    {
        public readonly double[] TokenScores = new double[queryTokenCount];
        public readonly TokenMatchKind[] TokenKinds = new TokenMatchKind[queryTokenCount];
        public readonly HashSet<int> Fields = [];
        public bool CompactExact;
        public bool CompactPrefix;
        public bool CompactContains;
    }
}
