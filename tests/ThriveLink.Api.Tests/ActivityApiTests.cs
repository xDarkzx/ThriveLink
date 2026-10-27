using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ThriveLink.Api.Tests;

public class ActivityApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static object Report(string member, int steps, string? date = null) => new
    {
        memberNumber = member,
        date = date ?? Today,
        steps,
        activeMinutes = 45,
        averageHeartRate = 140,
        provider = "AppleHealth",
    };

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Valid_activity_earns_points_and_shows_on_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("/api/activity", Report("TEST001", 10_500), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Accepted", body.GetProperty("outcome").GetString());
        Assert.Equal(100, body.GetProperty("steps").GetInt32());

        var member = await _client.GetFromJsonAsync<JsonElement>("/api/members/TEST001", ct);
        Assert.True(member.GetProperty("pointsThisYear").GetInt32() >= 100);
        Assert.Equal("Test M.", member.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Resending_a_day_replaces_it_instead_of_double_counting()
    {
        var ct = TestContext.Current.CancellationToken;
        var date = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await _client.PostAsJsonAsync("/api/activity", Report("TEST001", 5_000, date), ct);
        await _client.PostAsJsonAsync("/api/activity", Report("TEST001", 10_000, date), ct);

        var member = await _client.GetFromJsonAsync<JsonElement>("/api/members/TEST001", ct);
        var days = member.GetProperty("recentActivity").EnumerateArray().Where(d => d.GetProperty("date").GetString() == date).ToList();
        Assert.Single(days);
        Assert.Equal(10_000, days[0].GetProperty("steps").GetInt32());
    }

    [Fact]
    public async Task Implausible_steps_are_flagged()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("/api/activity", Report("TEST001", 150_000, DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Flagged", body.GetProperty("outcome").GetString());
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Unknown_member_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/activity", Report("NOPE", 1_000), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Future_date_is_rejected()
    {
        var future = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var response = await _client.PostAsJsonAsync("/api/activity", Report("TEST001", 1_000, future), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stats_returns_expected_sections()
    {
        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/stats", TestContext.Current.CancellationToken);
        foreach (var section in new[] { "members", "devicesByProvider", "statusCounts", "lastWeek", "dailyActivity", "syncHealth", "flaggedReadings", "topMembers" })
            Assert.True(stats.TryGetProperty(section, out _), $"missing {section}");
    }
}
