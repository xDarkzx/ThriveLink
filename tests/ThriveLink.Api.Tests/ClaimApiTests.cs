using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Api.Tests;

public class ClaimApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Day(int offset) => DateTime.UtcNow.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<string> SubmitAsync(string type = "Reimbursement", decimal amount = 180m, string provider = "Waikato Physio", int treatmentOffset = -5, string? description = null)
    {
        var response = await _client.PostAsJsonAsync("/api/claims", new
        {
            memberNumber = "TEST001",
            type,
            category = "Physiotherapy",
            providerName = provider,
            treatmentDate = Day(treatmentOffset),
            amount,
            description = description ?? "Six physio sessions for a knee injury",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("claimNumber").GetString()!;
    }

    private async Task<HttpResponseMessage> UploadAsync(string claimNumber, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await _client.PostAsync(new Uri($"/api/claims/{claimNumber}/documents", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> GetAsync(string claimNumber) =>
        await _client.GetFromJsonAsync<JsonElement>($"/api/claims/{claimNumber}", TestContext.Current.CancellationToken);

    private async Task TriageAsync(IClaimAssistant? assistant)
    {
        using var scope = factory.Services.CreateScope();
        var service = new ClaimTriageService(
            scope.ServiceProvider.GetRequiredService<ThriveLinkDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            assistant);
        await service.TriagePendingAsync(ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Submitted_claim_gets_a_number_and_a_due_date()
    {
        var number = await SubmitAsync();
        var claim = await GetAsync(number);
        Assert.StartsWith("CL-", number, StringComparison.Ordinal);
        Assert.Equal("Submitted", claim.GetProperty("status").GetString());
        Assert.True(claim.TryGetProperty("dueBy", out _));
    }

    [Fact]
    public async Task Invalid_claim_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/claims", new
        {
            memberNumber = "TEST001",
            type = "Reimbursement",
            category = "GpVisit",
            providerName = "",
            treatmentDate = Day(-1),
            amount = -5,
            description = "",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Document_upload_is_stored_under_a_server_generated_name()
    {
        var number = await SubmitAsync();
        var response = await UploadAsync(number, "../../etc/receipt.pdf", [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var saved = Assert.Single(factory.Documents.Saved, kv => kv.Key.StartsWith(number + "/", StringComparison.Ordinal));
        Assert.EndsWith(".pdf", saved.Key, StringComparison.Ordinal);
        Assert.DoesNotContain("..", saved.Key, StringComparison.Ordinal);
        Assert.Equal("application/pdf", saved.Value.ContentType);

        var claim = await GetAsync(number);
        Assert.Equal("receipt.pdf", claim.GetProperty("documents")[0].GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Disallowed_file_types_are_rejected()
    {
        var number = await SubmitAsync();
        var response = await UploadAsync(number, "invoice.exe", [1]);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rules_only_triage_asks_for_missing_receipt()
    {
        var number = await SubmitAsync(provider: "No Receipt Clinic");
        await TriageAsync(assistant: null);
        var claim = await GetAsync(number);
        Assert.Equal("Triaged", claim.GetProperty("status").GetString());
        Assert.Equal("NeedsInfo", claim.GetProperty("recommendation").GetString());
        Assert.Contains("No receipt", claim.GetProperty("ruleFlags").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_cannot_override_rules_to_be_less_cautious_and_never_sees_identifiers()
    {
        var assistant = new FakeAssistant(new AiTriage(TriageRecommendation.LikelyApprove, "Looks routine.", 0.9));
        var number = await SubmitAsync(amount: 9_000m, provider: "Big Surgery Ltd", description: "Contact me on 021 555 0199 or TEST001 re surgery");
        await UploadAsync(number, "invoice.pdf", [1]);
        await TriageAsync(assistant);

        var claim = await GetAsync(number);
        Assert.Equal("Review", claim.GetProperty("recommendation").GetString()); // high value: rules win
        Assert.Equal("Looks routine.", claim.GetProperty("aiSummary").GetString());

        var sent = Assert.Single(assistant.Received, r => r.Provider == "Big Surgery Ltd");
        Assert.DoesNotContain("021 555", sent.Description, StringComparison.Ordinal);
        Assert.Contains(sent.RuleFlags, f => f.StartsWith("High value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_ai_call_keeps_rules_triage()
    {
        var number = await SubmitAsync(provider: "Flaky AI Clinic");
        await UploadAsync(number, "r.png", [1]);
        await TriageAsync(new FakeAssistant(failure: new HttpRequestException("model unavailable")));

        var claim = await GetAsync(number);
        Assert.Equal("Triaged", claim.GetProperty("status").GetString());
        Assert.Equal("LikelyApprove", claim.GetProperty("recommendation").GetString());
        Assert.Equal(JsonValueKind.Null, claim.GetProperty("aiSummary").ValueKind);
    }

    [Fact]
    public async Task Decline_needs_a_note_and_a_decided_claim_cannot_be_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        var number = await SubmitAsync(provider: "Decision Clinic");

        var noNote = await _client.PostAsJsonAsync($"/api/claims/{number}/decision", new { decision = "Declined", assessor = "Assessor A" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);

        var approve = await _client.PostAsJsonAsync($"/api/claims/{number}/decision", new { decision = "Approved", assessor = "Assessor A", note = "Within policy" }, ct);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var again = await _client.PostAsJsonAsync($"/api/claims/{number}/decision", new { decision = "Declined", assessor = "Assessor B", note = "Changed mind" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    private sealed class FakeAssistant(AiTriage? result = null, Exception? failure = null) : IClaimAssistant
    {
        public List<RedactedClaim> Received { get; } = [];
        public string Model => "fake-model";

        public Task<AiTriage?> AssessAsync(RedactedClaim claim, CancellationToken ct)
        {
            Received.Add(claim);
            return failure is null ? Task.FromResult(result) : Task.FromException<AiTriage?>(failure);
        }
    }
}
