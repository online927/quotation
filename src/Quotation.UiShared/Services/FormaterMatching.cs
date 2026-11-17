using Quotation.Contracts;

namespace Quotation.Desktop.Services;

/// <summary>Formater (PC) and "New from a message" (phone): when a typed customer or item name is taken from Tally.</summary>
public static class FormaterMatching
{
    /// <summary>
    /// Taken from Tally only when the search makes it clear (an exact name or part number, a single match of every word, or a
    /// decisive lead); otherwise kept as typed, with the closest Tally entry offered. The user's own choice wins, then the
    /// entry chosen for the same typing before (remembered on the server).
    /// </summary>
    public static (TItem? Chosen, TItem? Suggestion, bool Remembered) Decide<THit, TItem>(string typed, List<THit> hits, Func<THit, int> id,
        Func<THit, SearchMatchDto> match, Func<THit, double> score, Dictionary<string, int> choices, int? remembered, Func<THit, TItem> item)
        where TItem : class
    {
        if (choices.TryGetValue(typed, out var chosen))
        {
            if (chosen < 0) return (null, hits.Count > 0 ? item(hits[0]) : null, false);
            var pick = hits.FirstOrDefault(h => id(h) == chosen);
            if (pick is not null) return (item(pick), null, chosen == remembered);
        }
        if (remembered is { } r && hits.FirstOrDefault(h => id(h) == r) is { } known) return (item(known), null, true);
        if (hits.Count == 0) return (null, null, false);
        var exact = hits.Where(h => match(h).ExactName || match(h).ExactIdentifier).ToList();
        if (exact.Count == 1) return (item(exact[0]), null, false);
        var complete = hits.Where(h => match(h).AllTermsMatched && !match(h).UsedFuzzy).OrderByDescending(score).ToList();
        if (exact.Count == 0 && complete.Count == 1) return (item(complete[0]), null, false);
        if (exact.Count == 0 && complete.Count > 1 && score(complete[0]) >= 1.6 * Math.Max(0.01, score(complete[1])))
            return (item(complete[0]), null, false);
        return (null, item((exact.Count > 0 ? exact : complete.Count > 0 ? complete : hits)[0]), false);
    }
}
