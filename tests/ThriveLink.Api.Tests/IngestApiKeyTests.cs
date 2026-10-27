using System.Net;
using System.Net.Http.Json;

namespace ThriveLink.Api.Tests;

public sealed class ApiKeyFactory : ApiFactory
{
    public const string Key = "test-ingest-key";
    protected override string? IngestApiKey => Key;
}

public class IngestApiKeyTests(ApiKeyFactory factory) : IClassFixture<ApiKeyFactory>
{
    private static object Report() => new
    {
        memberNumber = "TEST001",
        date = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        steps = 8_000,
        activeMinutes = 10,
        averageHeartRate = 90,
        provider = "Garmin",
    };

    [Fact]
    public async Task Missing_key_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/activity", Report(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_key_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        var response = await client.PostAsJsonAsync("/api/activity", Report(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Correct_key_is_accepted()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKeyFactory.Key);
        var response = await client.PostAsJsonAsync("/api/activity", Report(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Read_endpoints_do_not_need_the_key()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/api/stats", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
