using System.Globalization;

namespace ThriveLink.Domain.Tests;

public class PointsRulesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4_999, 0)]
    [InlineData(5_000, 30)]
    [InlineData(7_500, 60)]
    [InlineData(10_000, 100)]
    [InlineData(25_000, 100)]
    public void StepsPoints_follow_thresholds(int steps, int expected) =>
        Assert.Equal(expected, PointsRules.StepsPoints(steps));

    [Fact]
    public void Workout_needs_elevated_heart_rate()
    {
        // Age 40 => max HR 180, 60% = 108 bpm.
        Assert.Equal(0, PointsRules.WorkoutPoints(45, 100, 40));
        Assert.Equal(60, PointsRules.WorkoutPoints(45, 120, 40));
        Assert.Equal(100, PointsRules.WorkoutPoints(75, 120, 40));
    }

    [Fact]
    public void Workout_without_heart_rate_earns_nothing() =>
        Assert.Equal(0, PointsRules.WorkoutPoints(90, null, 40));

    [Fact]
    public void Day_points_are_capped_per_category()
    {
        var day = PointsRules.ForDay(steps: 40_000, activeMinutes: 300, averageHeartRate: 150, age: 30);
        Assert.Equal(100, day.Steps);
        Assert.Equal(100, day.Workout);
        Assert.Null(day.FlagReason);
    }

    [Fact]
    public void Implausible_steps_are_flagged_and_earn_nothing()
    {
        var day = PointsRules.ForDay(steps: 120_000, activeMinutes: 30, averageHeartRate: 130, age: 30);
        Assert.Equal(0, day.Total);
        Assert.Contains("Implausible step count", day.FlagReason);
    }

    [Fact]
    public void Negative_values_are_flagged()
    {
        var day = PointsRules.ForDay(steps: -5, activeMinutes: 0, averageHeartRate: null, age: 30);
        Assert.Equal(0, day.Total);
        Assert.NotNull(day.FlagReason);
    }

    [Theory]
    [InlineData(0, MemberStatus.Bronze)]
    [InlineData(9_999, MemberStatus.Bronze)]
    [InlineData(10_000, MemberStatus.Silver)]
    [InlineData(20_000, MemberStatus.Gold)]
    [InlineData(30_000, MemberStatus.Platinum)]
    public void Status_tiers(int points, MemberStatus expected) =>
        Assert.Equal(expected, PointsRules.StatusFor(points));

    [Fact]
    public void Higher_status_never_has_lower_discount()
    {
        var discounts = Enum.GetValues<MemberStatus>().Select(PointsRules.PremiumDiscountFor).ToList();
        Assert.Equal(discounts.OrderBy(d => d), discounts);
    }

    [Theory]
    [InlineData("2026-10-26", "2026-10-26")] // Monday
    [InlineData("2026-10-29", "2026-10-26")] // Thursday
    [InlineData("2026-11-01", "2026-10-26")] // Sunday
    public void Weeks_start_on_monday(string date, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), PointsRules.WeekStart(DateOnly.Parse(date, CultureInfo.InvariantCulture)));

    [Fact]
    public void Age_accounts_for_birthday_not_yet_reached()
    {
        Assert.Equal(39, PointsRules.AgeOn(new DateOnly(1986, 12, 1), new DateOnly(2026, 10, 27)));
        Assert.Equal(40, PointsRules.AgeOn(new DateOnly(1986, 10, 27), new DateOnly(2026, 10, 27)));
    }
}
