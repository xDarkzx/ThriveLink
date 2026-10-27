using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

/// <summary>What leaves our systems for AI review: no names, member or policy numbers, and free text is redacted.</summary>
public sealed record RedactedClaim(
    ClaimType Type,
    ClaimCategory Category,
    string Provider,
    DateOnly TreatmentDate,
    decimal Amount,
    string Description,
    int DocumentCount,
    IReadOnlyList<string> RuleFlags);

public sealed record AiTriage(TriageRecommendation Recommendation, string Summary, double Confidence);

/// <summary>An AI model that reviews a redacted claim. Implemented in the triage job; optional.</summary>
public interface IClaimAssistant
{
    string Model { get; }
    Task<AiTriage?> AssessAsync(RedactedClaim claim, CancellationToken ct);
}

public sealed record TriageRunSummary(int Triaged, int AiAssisted, int AiFailed);

/// <summary>
/// Triage = deterministic rule checks, then (optionally) AI on a redacted copy of the claim.
/// Guardrail: AI can make a recommendation more cautious than the rules, never less.
/// </summary>
public class ClaimTriageService(ThriveLinkDbContext db, TimeProvider clock, IClaimAssistant? assistant = null)
{
    public async Task<TriageRunSummary> TriagePendingAsync(int batchSize = 20, CancellationToken ct = default)
    {
        var pending = await db.Claims
            .Include(c => c.Documents)
            .Where(c => c.Status == ClaimStatus.Submitted && c.TriagedAt == null)
            .OrderBy(c => c.SubmittedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        int aiAssisted = 0, aiFailed = 0;

        foreach (var claim in pending)
        {
            var duplicateOf = await db.Claims
                .Where(o => o.Id != claim.Id && o.MemberId == claim.MemberId && o.ProviderName == claim.ProviderName
                            && o.TreatmentDate == claim.TreatmentDate && o.Amount == claim.Amount && o.Status != ClaimStatus.Declined)
                .Select(o => o.ClaimNumber)
                .FirstOrDefaultAsync(ct);

            var flags = ClaimRules.Flags(claim.Type, claim.TreatmentDate, claim.Amount, claim.Documents.Count, duplicateOf, today);
            var rulesRecommendation = ClaimRules.RecommendationFor(flags);

            claim.RuleFlags = flags.Count == 0 ? null : string.Join("; ", flags);
            claim.Recommendation = rulesRecommendation;
            claim.AiSummary = null;
            claim.AiConfidence = null;
            claim.AiModel = null;

            if (assistant is not null)
            {
                var redacted = new RedactedClaim(claim.Type, claim.Category, Redactor.Redact(claim.ProviderName), claim.TreatmentDate,
                    claim.Amount, Redactor.Redact(claim.Description), claim.Documents.Count, flags);
                try
                {
                    var ai = await assistant.AssessAsync(redacted, ct);
                    if (ai is not null)
                    {
                        claim.Recommendation = MoreCautious(rulesRecommendation, ai.Recommendation);
                        claim.AiSummary = ai.Summary;
                        claim.AiConfidence = Math.Clamp(ai.Confidence, 0, 1);
                        claim.AiModel = assistant.Model;
                        aiAssisted++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // AI is advisory: a failed call leaves the rules-based triage in place.
                    aiFailed++;
                }
            }

            claim.Status = ClaimStatus.Triaged;
            claim.TriagedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return new TriageRunSummary(pending.Count, aiAssisted, aiFailed);
    }

    // LikelyApprove < Review < NeedsInfo in caution order.
    private static TriageRecommendation MoreCautious(TriageRecommendation a, TriageRecommendation b) =>
        Caution(a) >= Caution(b) ? a : b;

    private static int Caution(TriageRecommendation r) => r switch
    {
        TriageRecommendation.LikelyApprove => 0,
        TriageRecommendation.Review => 1,
        _ => 2,
    };
}
