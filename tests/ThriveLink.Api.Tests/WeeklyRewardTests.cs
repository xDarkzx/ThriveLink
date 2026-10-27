using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Api.Tests;

public class WeeklyRewardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Member_who_hits_target_is_rewarded_and_rerun_does_not_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var week = PointsRules.WeekStart(DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(-14);

        // 7 days at 10,000 steps = 700 activity points against a target of 300.
        for (var d = 0; d < 7; d++)
        {
            var response = await client.PostAsJsonAsync("/api/activity", new
            {
                memberNumber = "TEST001",
                date = week.AddDays(d).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                steps = 10_000,
                activeMinutes = 0,
                provider = "Fitbit",
            }, ct);
            response.EnsureSuccessStatusCode();
        }

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WeeklyRewardService>();
        var first = await service.EvaluateWeekAsync(week, ct);
        var second = await service.EvaluateWeekAsync(week, ct);

        Assert.Equal(1, first.Achieved);
        Assert.Equal(first, second);

        var db = scope.ServiceProvider.GetRequiredService<ThriveLinkDbContext>();
        var rows = await db.WeeklyRewards.Where(w => w.WeekStart == week).ToListAsync(ct);
        var row = Assert.Single(rows);
        Assert.True(row.Achieved);
        Assert.Equal(700, row.Points);
    }

    [Fact]
    public async Task Week_must_start_on_monday()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WeeklyRewardService>();
        await Assert.ThrowsAsync<ArgumentException>(() => service.EvaluateWeekAsync(new DateOnly(2026, 10, 28), TestContext.Current.CancellationToken));
    }
}
