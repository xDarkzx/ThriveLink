namespace ThriveLink.Domain;

public sealed record DayPoints(int Steps, int Workout, string? FlagReason)
{
    public int Total => Steps + Workout;
}

/// <summary>
/// ThriveLink's points programme. Modelled on how insurer wellness programmes work
/// (daily caps per category, personal weekly targets, annual status tiers); the numbers are ThriveLink's own.
/// </summary>
public static class PointsRules
{
    public const int DailyCategoryCap = 100;
    public const int DefaultWeeklyTarget = 300;
    public const int MinWeeklyTarget = 200;
    public const int MaxWeeklyTarget = 500;

    /// <summary>Above this a day's step count is treated as implausible (e.g. a shaken tracker).</summary>
    public const int MaxPlausibleSteps = 60_000;

    public const int MaxPlausibleActiveMinutes = 16 * 60;

    public static int StepsPoints(int steps) => steps switch
    {
        >= 10_000 => 100,
        >= 7_500 => 60,
        >= 5_000 => 30,
        _ => 0,
    };

    /// <summary>Workout points need sustained elevated heart rate: at least 60% of age-predicted max.</summary>
    public static int WorkoutPoints(int activeMinutes, int? averageHeartRate, int age)
    {
        if (averageHeartRate is null || age <= 0) return 0;
        var maxHeartRate = 220 - age;
        if (averageHeartRate < 0.6 * maxHeartRate) return 0;
        return activeMinutes switch
        {
            >= 60 => 100,
            >= 30 => 60,
            _ => 0,
        };
    }

    public static DayPoints ForDay(int steps, int activeMinutes, int? averageHeartRate, int age)
    {
        if (steps < 0 || activeMinutes < 0)
            return new DayPoints(0, 0, "Negative values reported");
        if (steps > MaxPlausibleSteps)
            return new DayPoints(0, 0, $"Implausible step count ({steps:N0})");
        if (activeMinutes > MaxPlausibleActiveMinutes)
            return new DayPoints(0, 0, $"Implausible active minutes ({activeMinutes})");

        var stepPoints = Math.Min(StepsPoints(steps), DailyCategoryCap);
        var workoutPoints = Math.Min(WorkoutPoints(activeMinutes, averageHeartRate, age), DailyCategoryCap);
        return new DayPoints(stepPoints, workoutPoints, null);
    }

    public static MemberStatus StatusFor(int pointsThisYear) => pointsThisYear switch
    {
        >= 30_000 => MemberStatus.Platinum,
        >= 20_000 => MemberStatus.Gold,
        >= 10_000 => MemberStatus.Silver,
        _ => MemberStatus.Bronze,
    };

    /// <summary>Premium discount percentage earned by each status.</summary>
    public static decimal PremiumDiscountFor(MemberStatus status) => status switch
    {
        MemberStatus.Platinum => 15m,
        MemberStatus.Gold => 12.5m,
        MemberStatus.Silver => 10m,
        _ => 5m,
    };

    /// <summary>Weeks run Monday to Sunday.</summary>
    public static DateOnly WeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    public static int AgeOn(DateOnly dateOfBirth, DateOnly on)
    {
        var age = on.Year - dateOfBirth.Year;
        if (dateOfBirth > on.AddYears(-age)) age--;
        return age;
    }
}
