using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

public sealed record WeeklyRewardSummary(DateOnly WeekStart, int Evaluated, int Achieved);

/// <summary>
/// Checks every member's activity points for a completed week against their personal target.
/// Safe to re-run: results are upserted per member and week (late syncs can turn a miss into a hit).
/// </summary>
public class WeeklyRewardService(ThriveLinkDbContext db, TimeProvider clock)
{
    public async Task<WeeklyRewardSummary> EvaluateWeekAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        if (PointsRules.WeekStart(weekStart) != weekStart)
            throw new ArgumentException("Week must start on a Monday.", nameof(weekStart));

        var weekEnd = weekStart.AddDays(6);
        var members = await db.Members.Select(m => new { m.Id, m.WeeklyTarget }).ToListAsync(ct);
        var points = await db.PointsEntries
            .Where(p => p.Date >= weekStart && p.Date <= weekEnd && p.Category != PointsCategory.HealthCheck)
            .GroupBy(p => p.MemberId)
            .Select(g => new { MemberId = g.Key, Points = g.Sum(p => p.Points) })
            .ToDictionaryAsync(x => x.MemberId, x => x.Points, ct);
        var existing = await db.WeeklyRewards.Where(w => w.WeekStart == weekStart).ToDictionaryAsync(w => w.MemberId, ct);

        var now = clock.GetUtcNow();
        var achieved = 0;
        foreach (var m in members)
        {
            var total = points.GetValueOrDefault(m.Id);
            var hit = total >= m.WeeklyTarget;
            if (hit) achieved++;

            if (!existing.TryGetValue(m.Id, out var reward))
            {
                reward = new WeeklyReward { MemberId = m.Id, WeekStart = weekStart };
                db.WeeklyRewards.Add(reward);
            }
            reward.Points = total;
            reward.Target = m.WeeklyTarget;
            reward.Achieved = hit;
            reward.EvaluatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new WeeklyRewardSummary(weekStart, members.Count, achieved);
    }
}
