using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

/// <summary>
/// Synthetic members and activity so the stats page has something to show. Deterministic (fixed seed).
/// All names and numbers are made up; no real member data ever goes in here.
/// </summary>
public static class DemoSeeder
{
    private static readonly string[] FirstNames = ["Aroha", "James", "Mere", "Liam", "Sophie", "Tama", "Olivia", "Noah", "Ana", "Jack", "Hine", "Ella", "Wiremu", "Grace", "Ruby", "Leo", "Kiri", "Mason", "Isla", "Hunter"];
    private static readonly string[] LastNames = ["Smith", "Ngata", "Wilson", "Patel", "Brown", "Tane", "Taylor", "Walker", "Chen", "Te Rangi", "Kumar", "Clarke", "Harris", "Parata", "Young"];

    public static async Task SeedAsync(ThriveLinkDbContext db, TimeProvider clock, int members = 60, int days = 35, CancellationToken ct = default)
    {
        if (await db.Members.AnyAsync(ct)) return;

        var rng = new Random(42);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var providers = Enum.GetValues<DeviceProvider>();

        for (var i = 1; i <= members; i++)
        {
            var dob = today.AddYears(-rng.Next(22, 66)).AddDays(-rng.Next(0, 365));
            var fitness = rng.NextDouble(); // 0 = sedentary, 1 = very active
            var member = new Member
            {
                MemberNumber = $"TL{100000 + i}",
                FirstName = FirstNames[rng.Next(FirstNames.Length)],
                LastName = LastNames[rng.Next(LastNames.Length)],
                DateOfBirth = dob,
                PolicyNumber = $"POL-{rng.Next(1_000_000, 9_999_999)}",
                WeeklyTarget = Math.Clamp((int)Math.Round((200 + fitness * 300) / 50) * 50, PointsRules.MinWeeklyTarget, PointsRules.MaxWeeklyTarget),
                JoinedOn = today.AddDays(-rng.Next(60, 900)),
            };

            // Most members link one device; a few never link one.
            if (rng.NextDouble() > 0.1)
            {
                var provider = providers[WeightedProvider(rng)];
                member.Devices.Add(new LinkedDevice { Provider = provider, LinkedAt = clock.GetUtcNow().AddDays(-rng.Next(30, 400)) });
            }

            db.Members.Add(member);
            if (member.Devices.Count == 0) continue;

            var device = member.Devices[0];
            for (var d = days; d >= 1; d--)
            {
                var date = today.AddDays(-d);
                if (rng.NextDouble() < 0.08) continue; // forgot to wear it

                var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var steps = (int)Math.Max(0, 3_000 + fitness * 9_000 + (weekend ? 1_500 : 0) + (rng.NextDouble() - 0.5) * 5_000);
                var workout = rng.NextDouble() < 0.2 + fitness * 0.5;
                var activeMinutes = workout ? rng.Next(20, 90) : rng.Next(0, 20);
                int? heartRate = workout ? rng.Next(105, 160) : rng.Next(70, 100);
                if (rng.NextDouble() < 0.004) steps = rng.Next(80_000, 150_000); // the occasional shaken tracker

                var age = PointsRules.AgeOn(dob, date);
                var points = PointsRules.ForDay(steps, activeMinutes, heartRate, age);
                var received = new DateTimeOffset(date.ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero).AddHours(rng.Next(0, 30));

                member.Activity.Add(new ActivityDay { Date = date, Steps = steps, ActiveMinutes = activeMinutes, AverageHeartRate = heartRate, Provider = device.Provider, ReceivedAt = received, FlagReason = points.FlagReason });
                member.Points.Add(new PointsEntry { Date = date, Category = PointsCategory.Steps, Points = points.Steps });
                member.Points.Add(new PointsEntry { Date = date, Category = PointsCategory.Workout, Points = points.Workout });
                device.LastSyncAt = received;
            }

            // Some members have done a health check this year (1,000 points per measure, 4 measures).
            if (rng.NextDouble() < 0.35)
                member.Points.Add(new PointsEntry { Date = today.AddDays(-rng.Next(1, days)), Category = PointsCategory.HealthCheck, Points = 1_000 * rng.Next(1, 5) });

            // A few devices stopped syncing recently, so the sync-health view has something to show.
            if (rng.NextDouble() < 0.07) device.LastSyncAt = clock.GetUtcNow().AddDays(-rng.Next(3, 10));
        }

        await db.SaveChangesAsync(ct);
    }

    // Assumed device mix for the demo: Apple most common, then Fitbit, Garmin, Samsung.
    private static int WeightedProvider(Random rng) => rng.NextDouble() switch
    {
        < 0.45 => (int)DeviceProvider.AppleHealth,
        < 0.70 => (int)DeviceProvider.Fitbit,
        < 0.88 => (int)DeviceProvider.Garmin,
        _ => (int)DeviceProvider.Samsung,
    };
}
