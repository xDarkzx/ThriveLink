using System.Globalization;
using System.Text.Json;

using Anthropic;
using Anthropic.Models.Messages;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.ClaimsTriage;

/// <summary>
/// Claude reviews a redacted claim and returns a structured suggestion for the human assessor.
/// It only ever sees <see cref="RedactedClaim"/>: no names, member/policy numbers or contact details.
/// </summary>
public sealed class AnthropicClaimAssistant(AnthropicClient client) : IClaimAssistant
{
    public string Model => "claude-opus-5-5";

    private const string SystemPrompt =
        """
        You help a health insurance claims assessor in New Zealand prioritise their queue.
        You review one claim at a time. Personal details have been removed and replaced with tokens like [number].
        You never approve or decline claims; a human makes every decision. Your job is to:
        - summarise the claim in one or two plain sentences for the assessor,
        - recommend LikelyApprove (routine, consistent, nothing missing), Review (something an assessor should check),
          or NeedsInfo (the member must supply something before it can be assessed),
        - give your confidence from 0 to 1.
        The rule flags were produced by deterministic checks and are always correct; take them into account.
        Base your view only on the claim provided. Do not invent policy terms or coverage limits.
        """;

    private static readonly JsonOutputFormat Format = new()
    {
        Schema = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                summary = new { type = "string" },
                recommendation = new { type = "string", @enum = new[] { "LikelyApprove", "Review", "NeedsInfo" } },
                confidence = new { type = "number" },
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "summary", "recommendation", "confidence" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        },
    };

    public async Task<AiTriage?> AssessAsync(RedactedClaim claim, CancellationToken ct)
    {
        var userMessage = string.Create(CultureInfo.InvariantCulture, $"""
            Claim type: {claim.Type}
            Category: {claim.Category}
            Provider: {claim.Provider}
            Treatment date: {claim.TreatmentDate:yyyy-MM-dd}
            Amount: NZD {claim.Amount:N2}
            Documents attached: {claim.DocumentCount}
            Rule flags: {(claim.RuleFlags.Count == 0 ? "none" : string.Join("; ", claim.RuleFlags))}
            Member's description: {claim.Description}
            """);

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = SystemPrompt,
            OutputConfig = new OutputConfig { Effort = Effort.Low, Format = Format },
            Messages = [new UserMessageParam { Content = userMessage }],
        }, ct);

        // A refusal or truncated answer is treated as "no AI opinion"; the rules-based triage stands.
        if (response.StopReason != "end_turn") return null;

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        if (!Enum.TryParse<TriageRecommendation>(root.GetProperty("recommendation").GetString(), out var recommendation)) return null;

        return new AiTriage(recommendation, root.GetProperty("summary").GetString() ?? string.Empty, root.GetProperty("confidence").GetDouble());
    }
}
