using Quotation.Contracts;
namespace Quotation.AI;

/// <summary>
/// Deterministic rules deciding whether a product or customer may be used without asking the user.
/// The AI's own choice or confidence is never sufficient: a match is accepted only when the
/// application's search evidence makes it unambiguous (exact identifier/name/alias, a single candidate
/// matching every requested term without typo correction, or a decisive ranking margin), and the AI's
/// choice — when it made one — agrees.
/// </summary>
public static class MatchPolicy
{
    /// <summary>The best candidate must score at least this multiple of the runner-up to be decisive.</summary>
    public const double DecisiveMargin = 1.6;

    public sealed record Decision(ResolutionStatus Status, int? Id, string Reason);

    public static Decision DecideProduct(IReadOnlyList<ProductCandidate> candidates, int? aiChoice) =>
        Decide(candidates.Select(c => (c.Id, c.Evidence)).ToList(), aiChoice, "product");

    public static Decision DecideCustomer(IReadOnlyList<CustomerCandidate> candidates, int? aiChoice, int? emailMatchId = null)
    {
        if (emailMatchId is { } byEmail)
        {
            // The sender's e-mail address is registered on exactly one Tally ledger.
            return aiChoice is null || aiChoice == byEmail
                ? new Decision(ResolutionStatus.Matched, byEmail, "Sender e-mail matches the customer's e-mail in Tally.")
                : new Decision(ResolutionStatus.NeedsSelection, null,
                    "The sender's e-mail belongs to a different customer than the one named in the text.");
        }
        return Decide(candidates.Select(c => (c.Id, c.Evidence)).ToList(), aiChoice, "customer");
    }

    private static Decision Decide(List<(int Id, MatchEvidenceInfo Evidence)> candidates, int? aiChoice, string kind)
    {
        if (candidates.Count == 0)
        {
            return new Decision(ResolutionStatus.NotFound, null, $"No {kind} in Tally matches the request.");
        }

        Decision Accept(int id, string reason) =>
            aiChoice is null || aiChoice == id
                ? new Decision(ResolutionStatus.Matched, id, reason)
                : new Decision(ResolutionStatus.NeedsSelection, null,
                    $"The AI suggested a different {kind} than the search evidence supports; please choose.");

        var exact = candidates.Where(c => c.Evidence.ExactIdentifier || c.Evidence.ExactName).ToList();
        if (exact.Count == 1) return Accept(exact[0].Id, $"Exact {kind} name / part number / identifier match.");
        if (exact.Count > 1) return Ambiguous(exact.Count, kind);

        var complete = candidates.Where(c => c.Evidence.AllTermsMatched && !c.Evidence.UsedFuzzy).ToList();
        if (complete.Count == 1) return Accept(complete[0].Id, $"Only one {kind} matches every requested term.");
        if (complete.Count > 1)
        {
            var ordered = complete.OrderByDescending(c => c.Evidence.Score).ToList();
            if (ordered[0].Evidence.Score >= DecisiveMargin * Math.Max(0.01, ordered[1].Evidence.Score))
            {
                return Accept(ordered[0].Id, $"The {kind} ranks decisively above the alternatives.");
            }
            return Ambiguous(complete.Count, kind);
        }

        // Only partial or typo-corrected matches: always let the user confirm.
        return new Decision(ResolutionStatus.NeedsSelection, null,
            $"No {kind} matches all requested terms exactly; closest matches shown.");
    }

    private static Decision Ambiguous(int count, string kind) =>
        new(ResolutionStatus.NeedsSelection, null, $"{count} possible {kind}s found — please select the correct one.");
}
