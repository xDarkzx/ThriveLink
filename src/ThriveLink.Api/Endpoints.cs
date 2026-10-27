using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Api;

public static class Endpoints
{
    // Members are only ever shown as first name + last initial outside the member's own view.
    private static string DisplayName(Member m) => $"{m.FirstName} {m.LastName[0]}.";

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", async (ThriveLinkDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var today = Today(clock);
            var yearStart = new DateOnly(today.Year, 1, 1);
            var lastWeekStart = PointsRules.WeekStart(today).AddDays(-7);
            var lastWeekEnd = lastWeekStart.AddDays(6);
            var chartFrom = today.AddDays(-14);

            var members = await db.Members.AsNoTracking().ToListAsync(ct);
            var devices = await db.LinkedDevices.AsNoTracking().Where(d => d.RevokedAt == null).ToListAsync(ct);

            var pointsThisYear = await db.PointsEntries.AsNoTracking()
                .Where(p => p.Date >= yearStart)
                .GroupBy(p => p.MemberId)
                .Select(g => new { MemberId = g.Key, Points = g.Sum(p => p.Points) })
                .ToDictionaryAsync(x => x.MemberId, x => x.Points, ct);

            var activityPointsLastWeek = await db.PointsEntries.AsNoTracking()
                .Where(p => p.Date >= lastWeekStart && p.Date <= lastWeekEnd && p.Category != PointsCategory.HealthCheck)
                .GroupBy(p => p.MemberId)
                .Select(g => new { MemberId = g.Key, Points = g.Sum(p => p.Points) })
                .ToDictionaryAsync(x => x.MemberId, x => x.Points, ct);

            var daily = await db.ActivityDays.AsNoTracking()
                .Where(a => a.Date >= chartFrom && a.Date < today)
                .GroupBy(a => a.Date)
                .Select(g => new { Date = g.Key, AverageSteps = (int)g.Average(a => a.Steps), ActiveMembers = g.Count() })
                .OrderBy(x => x.Date)
                .ToListAsync(ct);

            var flagged = await db.ActivityDays.AsNoTracking()
                .Where(a => a.FlagReason != null && a.Date >= today.AddDays(-30))
                .OrderByDescending(a => a.Date)
                .Select(a => new { a.MemberId, a.Date, a.Steps, a.Provider, a.FlagReason })
                .ToListAsync(ct);

            var byId = members.ToDictionary(m => m.Id);
            var statusOf = members.ToDictionary(m => m.Id, m => PointsRules.StatusFor(pointsThisYear.GetValueOrDefault(m.Id)));
            var hitTarget = members.Count(m => activityPointsLastWeek.GetValueOrDefault(m.Id) >= m.WeeklyTarget);

            return Results.Ok(new
            {
                GeneratedAt = now,
                Members = new { Total = members.Count, WithDevice = devices.Select(d => d.MemberId).Distinct().Count() },
                DevicesByProvider = devices.GroupBy(d => d.Provider).Select(g => new { Provider = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count),
                StatusCounts = Enum.GetValues<MemberStatus>().Select(s => new { Status = s, Count = statusOf.Values.Count(v => v == s), DiscountPercent = PointsRules.PremiumDiscountFor(s) }),
                LastWeek = new { WeekStart = lastWeekStart, MembersHitTarget = hitTarget, HitRate = members.Count == 0 ? 0 : Math.Round(100.0 * hitTarget / members.Count, 1) },
                DailyActivity = daily,
                SyncHealth = new
                {
                    Fresh = devices.Count(d => d.LastSyncAt >= now.AddHours(-24)),
                    Delayed = devices.Count(d => d.LastSyncAt < now.AddHours(-24) && d.LastSyncAt >= now.AddHours(-72)),
                    Stale = devices.Count(d => d.LastSyncAt is null || d.LastSyncAt < now.AddHours(-72)),
                },
                FlaggedReadings = flagged.Select(f => new { MemberNumber = byId[f.MemberId].MemberNumber, f.Date, f.Steps, f.Provider, f.FlagReason }),
                TopMembers = members
                    .OrderByDescending(m => pointsThisYear.GetValueOrDefault(m.Id))
                    .Take(10)
                    .Select(m => new
                    {
                        m.MemberNumber,
                        Name = DisplayName(m),
                        Points = pointsThisYear.GetValueOrDefault(m.Id),
                        Status = statusOf[m.Id],
                        DiscountPercent = PointsRules.PremiumDiscountFor(statusOf[m.Id]),
                    }),
            });
        });
    }

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/members/{memberNumber}", async (string memberNumber, ThriveLinkDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var member = await db.Members.AsNoTracking().Include(m => m.Devices).SingleOrDefaultAsync(m => m.MemberNumber == memberNumber, ct);
            if (member is null) return Results.NotFound();

            var today = Today(clock);
            var weekStart = PointsRules.WeekStart(today);
            var points = await db.PointsEntries.AsNoTracking()
                .Where(p => p.MemberId == member.Id && p.Date >= new DateOnly(today.Year, 1, 1))
                .ToListAsync(ct);
            var recent = await db.ActivityDays.AsNoTracking()
                .Where(a => a.MemberId == member.Id && a.Date >= today.AddDays(-14))
                .OrderBy(a => a.Date)
                .Select(a => new { a.Date, a.Steps, a.ActiveMinutes, a.AverageHeartRate, a.Provider, a.FlagReason })
                .ToListAsync(ct);

            var total = points.Sum(p => p.Points);
            var status = PointsRules.StatusFor(total);
            return Results.Ok(new
            {
                member.MemberNumber,
                Name = DisplayName(member),
                Status = status,
                PointsThisYear = total,
                DiscountPercent = PointsRules.PremiumDiscountFor(status),
                ThisWeek = new
                {
                    WeekStart = weekStart,
                    Points = points.Where(p => p.Date >= weekStart && p.Category != PointsCategory.HealthCheck).Sum(p => p.Points),
                    Target = member.WeeklyTarget,
                },
                Devices = member.Devices.Select(d => new { d.Provider, d.LinkedAt, d.LastSyncAt, Active = d.RevokedAt == null }),
                RecentActivity = recent,
            });
        });
    }

    public sealed record ActivityRequest(string MemberNumber, DateOnly Date, int Steps, int ActiveMinutes, int? AverageHeartRate, DeviceProvider Provider);

    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/activity", async (ActivityRequest req, ActivityIngestService ingest, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.MemberNumber))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["memberNumber"] = ["Required."] });

            var result = await ingest.IngestAsync(new ActivityReport(req.MemberNumber, req.Date, req.Steps, req.ActiveMinutes, req.AverageHeartRate, req.Provider), ct);
            return result.Outcome switch
            {
                IngestOutcome.MemberNotFound => Results.NotFound(new { error = "Unknown member" }),
                IngestOutcome.FutureDate => Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Date is in the future."] }),
                _ => Results.Ok(new { result.Outcome, result.Points!.Steps, result.Points.Workout, result.Points.Total, result.Points.FlagReason }),
            };
        }).AddEndpointFilter(RequireIngestApiKey);
    }

    /// <summary>
    /// Ingest callers (API Management, Functions) must send X-Api-Key matching Ingest:ApiKey from Key Vault.
    /// When no key is configured (local dev, tests) the check is skipped.
    /// </summary>
    private static async ValueTask<object?> RequireIngestApiKey(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Ingest:ApiKey"];
        if (string.IsNullOrEmpty(expected)) return await next(ctx);

        var supplied = ctx.HttpContext.Request.Headers["X-Api-Key"].ToString();
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
        return ok ? await next(ctx) : Results.Unauthorized();
    }
}
