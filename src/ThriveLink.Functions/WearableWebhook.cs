using System.Text.Json;
using System.Text.Json.Serialization;

using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Functions;

public sealed record WearableDay(DateOnly Date, int Steps, int ActiveMinutes, int? AverageHeartRate);

/// <summary>Provider-neutral batch, the shape an aggregator or our phone app sends after a sync.</summary>
public sealed record WearableBatch(string MemberNumber, DeviceProvider Provider, IReadOnlyList<WearableDay> Days);

/// <summary>
/// Receives synced activity from device providers. Archives the raw payload to Blob (audit trail and replay),
/// then ingests each day through the same rules the API uses.
/// </summary>
public class WearableWebhook(ActivityIngestService ingest, IServiceProvider services, ILogger<WearableWebhook> log)
{
    private const int MaxDaysPerBatch = 31;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Function("WearableWebhook")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "wearables/sync")] HttpRequest req,
        CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(req.Body))
            body = await reader.ReadToEndAsync(ct);

        WearableBatch? batch;
        try { batch = JsonSerializer.Deserialize<WearableBatch>(body, Json); }
        catch (JsonException ex)
        {
            log.MalformedPayload(ex);
            return new BadRequestObjectResult(new { error = "Malformed JSON" });
        }

        if (batch is null || string.IsNullOrWhiteSpace(batch.MemberNumber) || batch.Days is null || batch.Days.Count == 0)
            return new BadRequestObjectResult(new { error = "memberNumber, provider and at least one day are required" });
        if (batch.Days.Count > MaxDaysPerBatch)
            return new BadRequestObjectResult(new { error = $"At most {MaxDaysPerBatch} days per batch" });

        await ArchiveAsync(batch, body, ct);

        var outcomes = new List<object>();
        foreach (var day in batch.Days.OrderBy(d => d.Date))
        {
            var result = await ingest.IngestAsync(new ActivityReport(batch.MemberNumber, day.Date, day.Steps, day.ActiveMinutes, day.AverageHeartRate, batch.Provider), ct);
            if (result.Outcome == IngestOutcome.MemberNotFound)
            {
                log.UnknownMember(batch.MemberNumber, batch.Provider);
                return new NotFoundObjectResult(new { error = "Unknown member" });
            }
            outcomes.Add(new { day.Date, Outcome = result.Outcome.ToString(), Points = result.Points?.Total ?? 0, result.Points?.FlagReason });
        }

        log.WearableSynced(batch.MemberNumber, batch.Provider, batch.Days.Count);
        return new OkObjectResult(new { batch.MemberNumber, Days = outcomes });
    }

    private async Task ArchiveAsync(WearableBatch batch, string body, CancellationToken ct)
    {
        var blobs = services.GetService<BlobServiceClient>();
        if (blobs is null) return;

        var now = DateTime.UtcNow;
        var name = $"{batch.Provider}/{now:yyyy/MM/dd}/{batch.MemberNumber}-{now:HHmmssfff}-{Guid.NewGuid():N}.json";
        await blobs.GetBlobContainerClient("activity-raw").UploadBlobAsync(name, BinaryData.FromString(body), ct);
    }
}
