using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Functions;

public class ScheduledJobs(WeeklyRewardService rewards, ThriveLinkDbContext db, TimeProvider clock, ILogger<ScheduledJobs> log)
{
    /// <summary>
    /// Mondays 13:00 UTC Sunday = Monday 01:00/02:00 NZ. Evaluates the week that just ended,
    /// and re-evaluates the week before in case devices synced late.
    /// </summary>
    [Function("WeeklyRewards")]
    public async Task WeeklyRewards([TimerTrigger("0 0 13 * * Sun")] TimerInfo timer, CancellationToken ct)
    {
        var nzToday = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(13));
        var lastWeek = PointsRules.WeekStart(nzToday).AddDays(-7);
        foreach (var week in new[] { lastWeek.AddDays(-7), lastWeek })
        {
            var summary = await rewards.EvaluateWeekAsync(week, ct);
            log.WeeklyRewardsEvaluated(summary.WeekStart, summary.Achieved, summary.Evaluated);
        }
    }

    /// <summary>
    /// Daily at 19:00 UTC (morning in NZ). Logs the number of linked devices that haven't synced in 72 hours
    /// as a structured value (StaleDeviceCount) so an Azure Monitor log alert can fire on it.
    /// </summary>
    [Function("StaleDeviceSweep")]
    public async Task StaleDeviceSweep([TimerTrigger("0 0 19 * * *")] TimerInfo timer, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddHours(-72);
        var stale = await db.LinkedDevices
            .Where(d => d.RevokedAt == null && (d.LastSyncAt == null || d.LastSyncAt < cutoff))
            .GroupBy(d => d.Provider)
            .Select(g => new { Provider = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = stale.Sum(s => s.Count);
        log.StaleDevices(total);
        foreach (var s in stale)
            log.StaleDevicesForProvider(s.Provider, s.Count);
    }
}
