namespace ThriveLink.Domain;

public enum ClaimType
{
    /// <summary>Approval before treatment; the insurer pays the provider directly.</summary>
    PreApproval,

    /// <summary>The member has paid and claims the money back with receipts.</summary>
    Reimbursement,
}

public enum ClaimCategory
{
    GpVisit,
    Specialist,
    Diagnostics,
    Surgery,
    Physiotherapy,
    Prescription,
}

public enum ClaimStatus
{
    Submitted,
    Triaged,
    NeedsInfo,
    Approved,
    Declined,
}

/// <summary>What the triage step suggests. A human assessor always makes the decision.</summary>
public enum TriageRecommendation
{
    LikelyApprove,
    Review,
    NeedsInfo,
}

public class Claim
{
    public int Id { get; set; }
    public required string ClaimNumber { get; set; }
    public int MemberId { get; set; }
    public ClaimType Type { get; set; }
    public ClaimCategory Category { get; set; }
    public required string ProviderName { get; set; }
    public DateOnly TreatmentDate { get; set; }
    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public ClaimStatus Status { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateOnly DueBy { get; set; }

    // Triage (rules + optional AI). Suggestions only.
    public string? RuleFlags { get; set; }
    public TriageRecommendation? Recommendation { get; set; }
    public string? AiSummary { get; set; }
    public double? AiConfidence { get; set; }
    public string? AiModel { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }

    // Human decision.
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public List<ClaimDocument> Documents { get; set; } = [];
}

public class ClaimDocument
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public required string BlobName { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public static class ClaimRules
{
    /// <summary>Claims above this need a senior assessor regardless of what triage suggests.</summary>
    public const decimal HighValueThreshold = 5_000m;

    public const int AssessmentWorkingDays = 5;

    /// <summary>Assessment due date: 5 working days after submission (weekends skipped; public holidays not modelled).</summary>
    public static DateOnly DueBy(DateOnly submitted)
    {
        var date = submitted;
        var added = 0;
        while (added < AssessmentWorkingDays)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) added++;
        }
        return date;
    }

    /// <summary>
    /// Deterministic checks that run before (and regardless of) any AI. Each flag is something an assessor must look at.
    /// </summary>
    public static IReadOnlyList<string> Flags(ClaimType type, DateOnly treatmentDate, decimal amount, int documentCount, string? duplicateOf, DateOnly today)
    {
        var flags = new List<string>();
        if (amount <= 0) flags.Add("Amount must be positive");
        if (amount > HighValueThreshold) flags.Add($"High value (over {HighValueThreshold:N0}): senior assessor required");
        if (type == ClaimType.Reimbursement && documentCount == 0) flags.Add("No receipt or invoice attached");
        if (type == ClaimType.Reimbursement && treatmentDate > today) flags.Add("Reimbursement for treatment in the future");
        if (type == ClaimType.PreApproval && treatmentDate < today) flags.Add("Pre-approval requested after treatment date");
        if (treatmentDate < today.AddMonths(-12)) flags.Add("Treatment more than 12 months ago");
        if (duplicateOf is not null) flags.Add($"Possible duplicate of {duplicateOf}");
        return flags;
    }

    /// <summary>Rules-only recommendation, used when AI is unavailable and as a floor when it is available.</summary>
    public static TriageRecommendation RecommendationFor(IReadOnlyList<string> flags) => flags switch
    {
        [] => TriageRecommendation.LikelyApprove,
        _ when flags.Any(f => f.StartsWith("No receipt", StringComparison.Ordinal)) => TriageRecommendation.NeedsInfo,
        _ => TriageRecommendation.Review,
    };
}
