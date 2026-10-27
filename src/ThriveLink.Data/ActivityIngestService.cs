using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

public sealed record ActivityReport(string MemberNumber, DateOnly Date, int Steps, int ActiveMinutes, int? AverageHeartRate, DeviceProvider Provider);

public enum IngestOutcome
{
    Accepted,
    Flagged,
    MemberNotFound,
    FutureDate,
}

public sealed record IngestResult(IngestOutcome Outcome, DayPoints? Points = null);

/// <summary>
/// Upserts a day of activity and recalculates that day's points.
/// Devices re-send the same day as it fills up, so the latest report for a day wins.
/// </summary>
public class ActivityIngestService(ThriveLinkDbContext db, TimeProvider clock)
{
    public async Task<IngestResult> IngestAsync(ActivityReport report, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        // Allow one day ahead: NZ is up to 13 hours ahead of UTC.
        if (report.Date > today.AddDays(1)) return new IngestResult(IngestOutcome.FutureDate);

        var member = await db.Members.SingleOrDefaultAsync(m => m.MemberNumber == report.MemberNumber, ct);
        if (member is null) return new IngestResult(IngestOutcome.MemberNotFound);

        var day = await db.ActivityDays.SingleOrDefaultAsync(a => a.MemberId == member.Id && a.Date == report.Date, ct);
        var points = PointsRules.ForDay(report.Steps, report.ActiveMinutes, report.AverageHeartRate, PointsRules.AgeOn(member.DateOfBirth, report.Date));

        day ??= db.ActivityDays.Add(new ActivityDay { MemberId = member.Id, Date = report.Date }).Entity;
        day.Steps = report.Steps;
        day.ActiveMinutes = report.ActiveMinutes;
        day.AverageHeartRate = report.AverageHeartRate;
        day.Provider = report.Provider;
        day.ReceivedAt = now;
        day.FlagReason = points.FlagReason;

        await UpsertPointsAsync(member.Id, report.Date, PointsCategory.Steps, points.Steps, ct);
        await UpsertPointsAsync(member.Id, report.Date, PointsCategory.Workout, points.Workout, ct);

        var device = await db.LinkedDevices.FirstOrDefaultAsync(d => d.MemberId == member.Id && d.Provider == report.Provider && d.RevokedAt == null, ct);
        if (device is not null) device.LastSyncAt = now;

        await db.SaveChangesAsync(ct);
        return new IngestResult(points.FlagReason is null ? IngestOutcome.Accepted : IngestOutcome.Flagged, points);
    }

    private async Task UpsertPointsAsync(int memberId, DateOnly date, PointsCategory category, int points, CancellationToken ct)
    {
        var entry = await db.PointsEntries.SingleOrDefaultAsync(p => p.MemberId == memberId && p.Date == date && p.Category == category, ct);
        if (entry is null)
            db.PointsEntries.Add(new PointsEntry { MemberId = memberId, Date = date, Category = category, Points = points });
        else
            entry.Points = points;
    }
}
