using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Api.Tests;

/// <summary>Exercises dbo.usp_ApplyHealthChecks, the stored procedure Data Factory runs after loading a partner file.</summary>
public class HealthCheckImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string CheckDate = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string Future = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task LoadStagingAndApplyAsync(ThriveLinkDbContext db, CancellationToken ct)
    {
        // Same shape as a partner CSV landing in staging.HealthCheckImport via the Data Factory copy activity.
        // ExecuteSqlAsync turns each interpolated value into a SQL parameter.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO staging.HealthCheckImport (MemberNumber, CheckDate, Measure, Partner) VALUES
              (' TEST001 ', {CheckDate}, 'BMI', 'Partner Pharmacy'),
              ('TEST001', {CheckDate}, 'BloodPressure', 'Partner Pharmacy'),
              ('TEST001', {CheckDate}, 'BloodPressure', 'Partner Pharmacy'),
              ('NOBODY', {CheckDate}, 'BMI', 'Partner Pharmacy'),
              ('TEST001', 'not-a-date', 'BMI', 'Partner Pharmacy'),
              ('TEST001', {Future}, 'BMI', 'Partner Pharmacy'),
              ('TEST001', {CheckDate}, 'Eyesight', 'Partner Pharmacy')
            """, ct);
        await db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_ApplyHealthChecks", ct);
    }

    [Fact]
    public async Task Valid_rows_award_points_and_bad_rows_are_rejected_with_reasons()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThriveLinkDbContext>();

        await LoadStagingAndApplyAsync(db, ct);

        var member = await db.Members.SingleAsync(m => m.MemberNumber == "TEST001", ct);
        var date = DateOnly.Parse(CheckDate, System.Globalization.CultureInfo.InvariantCulture);

        var results = await db.HealthCheckResults.Where(h => h.MemberId == member.Id).ToListAsync(ct);
        Assert.Equal(["BloodPressure", "BMI"], results.Select(r => r.Measure).Order());

        var points = await db.PointsEntries.SingleAsync(p => p.MemberId == member.Id && p.Date == date && p.Category == PointsCategory.HealthCheck, ct);
        Assert.Equal(2_000, points.Points);

        var reasons = await db.HealthCheckRejects.Select(r => r.Reason).ToListAsync(ct);
        Assert.Contains("Unknown member", reasons);
        Assert.Contains("Invalid date", reasons);
        Assert.Contains("Date in the future", reasons);
        Assert.Contains("Unknown measure", reasons);

        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM staging.HealthCheckImport").SingleAsync(ct));

        // Re-importing the same file must not double-count.
        await LoadStagingAndApplyAsync(db, ct);
        Assert.Equal(2, await db.HealthCheckResults.CountAsync(h => h.MemberId == member.Id, ct));
        var again = await db.PointsEntries.AsNoTracking().SingleAsync(p => p.MemberId == member.Id && p.Date == date && p.Category == PointsCategory.HealthCheck, ct);
        Assert.Equal(2_000, again.Points);
    }
}
