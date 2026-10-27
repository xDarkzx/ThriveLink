namespace ThriveLink.Domain;

public enum DeviceProvider
{
    AppleHealth,
    Fitbit,
    Garmin,
    Samsung,
}

public enum PointsCategory
{
    Steps,
    Workout,
    HealthCheck,
}

public enum MemberStatus
{
    Bronze,
    Silver,
    Gold,
    Platinum,
}

public class Member
{
    public int Id { get; set; }
    public required string MemberNumber { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public required string PolicyNumber { get; set; }
    public int WeeklyTarget { get; set; } = PointsRules.DefaultWeeklyTarget;
    public DateOnly JoinedOn { get; set; }

    public List<LinkedDevice> Devices { get; set; } = [];
    public List<ActivityDay> Activity { get; set; } = [];
    public List<PointsEntry> Points { get; set; } = [];
}

public class LinkedDevice
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public DeviceProvider Provider { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
}

/// <summary>One member's activity for one day, as last reported by their device.</summary>
public class ActivityDay
{
    public long Id { get; set; }
    public int MemberId { get; set; }
    public DateOnly Date { get; set; }
    public int Steps { get; set; }
    public int ActiveMinutes { get; set; }
    public int? AverageHeartRate { get; set; }
    public DeviceProvider Provider { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Set when the reading fails plausibility checks; points are withheld until reviewed.</summary>
    public string? FlagReason { get; set; }
}

/// <summary>Points ledger: at most one row per member, day and category.</summary>
public class PointsEntry
{
    public long Id { get; set; }
    public int MemberId { get; set; }
    public DateOnly Date { get; set; }
    public PointsCategory Category { get; set; }
    public int Points { get; set; }
}
