using Microsoft.EntityFrameworkCore;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Api;

public static class ClaimEndpoints
{
    private const long MaxDocumentBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    public sealed record SubmitClaimRequest(string MemberNumber, ClaimType Type, ClaimCategory Category, string ProviderName, DateOnly TreatmentDate, decimal Amount, string Description);

    public sealed record DecisionRequest(ClaimStatus Decision, string Assessor, string? Note);

    public static void MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var claims = app.MapGroup("/api/claims");

        claims.MapPost("/", async (SubmitClaimRequest req, ClaimService service, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.MemberNumber)) errors["memberNumber"] = ["Required."];
            if (string.IsNullOrWhiteSpace(req.ProviderName) || req.ProviderName.Length > 150) errors["providerName"] = ["Required, at most 150 characters."];
            if (string.IsNullOrWhiteSpace(req.Description) || req.Description.Length > 2000) errors["description"] = ["Required, at most 2000 characters."];
            if (req.Amount <= 0 || req.Amount > 1_000_000) errors["amount"] = ["Must be between 0 and 1,000,000."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var claim = await service.SubmitAsync(new ClaimSubmission(req.MemberNumber, req.Type, req.Category, req.ProviderName, req.TreatmentDate, req.Amount, req.Description), ct);
            return claim is null
                ? Results.NotFound(new { error = "Unknown member" })
                : Results.Created($"/api/claims/{claim.ClaimNumber}", new { claim.ClaimNumber, claim.Status, claim.DueBy });
        });

        claims.MapPost("/{claimNumber}/documents", async (string claimNumber, IFormFile file, ClaimService service, IDocumentStore store, CancellationToken ct) =>
        {
            if (!store.IsConfigured) return Results.Problem("Document storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (file.Length == 0 || file.Length > MaxDocumentBytes)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["File must be between 1 byte and 10 MB."] });

            // Trust the extension allow-list, not the client's Content-Type, and never use the client's file name as the blob path.
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedTypes.TryGetValue(extension, out var contentType))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Only PDF, JPG and PNG are accepted."] });

            var blobName = $"{claimNumber}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var safeFileName = Path.GetFileName(file.FileName);
            if (safeFileName.Length > 200) safeFileName = safeFileName[^200..];

            // Check the claim exists before storing anything.
            var exists = await service.AddDocumentAsync(claimNumber, safeFileName, contentType, file.Length, blobName, ct);
            if (exists is null) return Results.NotFound(new { error = "Unknown claim" });

            await using var stream = file.OpenReadStream();
            await store.SaveAsync(blobName, stream, contentType, ct);
            return Results.Created($"/api/claims/{claimNumber}", new { exists.FileName, exists.SizeBytes });
        }).DisableAntiforgery();

        // Assessor queue. Members are shown as first name + last initial.
        claims.MapGet("/", async (ClaimStatus? status, ThriveLinkDbContext db, CancellationToken ct) =>
        {
            var query = db.Claims.AsNoTracking();
            if (status is not null) query = query.Where(c => c.Status == status);
            var rows = await query
                .OrderBy(c => c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Declined)
                .ThenBy(c => c.DueBy)
                .Take(200)
                .Join(db.Members, c => c.MemberId, m => m.Id, (c, m) => new
                {
                    c.ClaimNumber,
                    m.FirstName,
                    m.LastName,
                    c.Type,
                    c.Category,
                    c.ProviderName,
                    c.TreatmentDate,
                    c.Amount,
                    c.Status,
                    c.DueBy,
                    c.Recommendation,
                    c.RuleFlags,
                    c.AiSummary,
                    c.AiConfidence,
                    Documents = c.Documents.Count,
                })
                .ToListAsync(ct);
            return Results.Ok(rows.Select(r => new
            {
                r.ClaimNumber,
                Member = $"{r.FirstName} {r.LastName[0]}.",
                r.Type,
                r.Category,
                r.ProviderName,
                r.TreatmentDate,
                r.Amount,
                r.Status,
                r.DueBy,
                r.Recommendation,
                r.RuleFlags,
                r.AiSummary,
                r.AiConfidence,
                r.Documents,
            }));
        });

        claims.MapGet("/{claimNumber}", async (string claimNumber, ThriveLinkDbContext db, CancellationToken ct) =>
        {
            var claim = await db.Claims.AsNoTracking().Include(c => c.Documents).SingleOrDefaultAsync(c => c.ClaimNumber == claimNumber, ct);
            if (claim is null) return Results.NotFound();
            return Results.Ok(new
            {
                claim.ClaimNumber,
                claim.Type,
                claim.Category,
                claim.ProviderName,
                claim.TreatmentDate,
                claim.Amount,
                claim.Description,
                claim.Status,
                claim.SubmittedAt,
                claim.DueBy,
                claim.RuleFlags,
                claim.Recommendation,
                claim.AiSummary,
                claim.AiConfidence,
                claim.AiModel,
                claim.TriagedAt,
                claim.DecidedBy,
                claim.DecisionNote,
                claim.DecidedAt,
                Documents = claim.Documents.Select(d => new { d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt }),
            });
        });

        claims.MapPost("/{claimNumber}/decision", async (string claimNumber, DecisionRequest req, ClaimService service, CancellationToken ct) =>
            await service.DecideAsync(claimNumber, req.Decision, req.Assessor, req.Note, ct) switch
            {
                DecisionOutcome.Recorded => Results.Ok(new { claimNumber, req.Decision }),
                DecisionOutcome.NotFound => Results.NotFound(),
                DecisionOutcome.AlreadyDecided => Results.Conflict(new { error = "Claim already decided" }),
                _ => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["decision"] = ["Decision must be Approved, Declined or NeedsInfo, with an assessor; a decline needs a note."],
                }),
            });
    }
}
