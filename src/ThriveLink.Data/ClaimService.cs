using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

public sealed record ClaimSubmission(string MemberNumber, ClaimType Type, ClaimCategory Category, string ProviderName, DateOnly TreatmentDate, decimal Amount, string Description);

public enum DecisionOutcome
{
    Recorded,
    NotFound,
    AlreadyDecided,
    InvalidDecision,
}

/// <summary>Member-facing submission and the assessor's decision. Triage lives in <see cref="ClaimTriageService"/>.</summary>
public class ClaimService(ThriveLinkDbContext db, TimeProvider clock)
{
    public async Task<Claim?> SubmitAsync(ClaimSubmission s, CancellationToken ct = default)
    {
        var member = await db.Members.SingleOrDefaultAsync(m => m.MemberNumber == s.MemberNumber, ct);
        if (member is null) return null;

        var now = clock.GetUtcNow();
        var claim = new Claim
        {
            ClaimNumber = await NewClaimNumberAsync(now, ct),
            MemberId = member.Id,
            Type = s.Type,
            Category = s.Category,
            ProviderName = s.ProviderName.Trim(),
            TreatmentDate = s.TreatmentDate,
            Amount = decimal.Round(s.Amount, 2),
            Description = s.Description.Trim(),
            Status = ClaimStatus.Submitted,
            SubmittedAt = now,
            DueBy = ClaimRules.DueBy(DateOnly.FromDateTime(now.UtcDateTime)),
        };
        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);
        return claim;
    }

    public async Task<ClaimDocument?> AddDocumentAsync(string claimNumber, string fileName, string contentType, long size, string blobName, CancellationToken ct = default)
    {
        var claim = await db.Claims.SingleOrDefaultAsync(c => c.ClaimNumber == claimNumber, ct);
        if (claim is null) return null;

        var doc = new ClaimDocument { ClaimId = claim.Id, FileName = fileName, ContentType = contentType, SizeBytes = size, BlobName = blobName, UploadedAt = clock.GetUtcNow() };
        db.ClaimDocuments.Add(doc);

        // New evidence invalidates earlier triage; send it back through the queue.
        if (claim.Status is ClaimStatus.Triaged or ClaimStatus.NeedsInfo)
        {
            claim.Status = ClaimStatus.Submitted;
            claim.TriagedAt = null;
        }
        await db.SaveChangesAsync(ct);
        return doc;
    }

    /// <summary>The decision is always a person's. Triage output is advice they can accept or ignore.</summary>
    public async Task<DecisionOutcome> DecideAsync(string claimNumber, ClaimStatus decision, string assessor, string? note, CancellationToken ct = default)
    {
        if (decision is not (ClaimStatus.Approved or ClaimStatus.Declined or ClaimStatus.NeedsInfo) || string.IsNullOrWhiteSpace(assessor))
            return DecisionOutcome.InvalidDecision;
        if (decision == ClaimStatus.Declined && string.IsNullOrWhiteSpace(note))
            return DecisionOutcome.InvalidDecision; // a decline must say why

        var claim = await db.Claims.SingleOrDefaultAsync(c => c.ClaimNumber == claimNumber, ct);
        if (claim is null) return DecisionOutcome.NotFound;
        if (claim.Status is ClaimStatus.Approved or ClaimStatus.Declined) return DecisionOutcome.AlreadyDecided;

        claim.Status = decision;
        claim.DecidedBy = assessor.Trim();
        claim.DecisionNote = note?.Trim();
        claim.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return DecisionOutcome.Recorded;
    }

    private async Task<string> NewClaimNumberAsync(DateTimeOffset now, CancellationToken ct)
    {
        while (true)
        {
            var number = $"CL-{now:yyMMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}";
            if (!await db.Claims.AnyAsync(c => c.ClaimNumber == number, ct)) return number;
        }
    }
}
