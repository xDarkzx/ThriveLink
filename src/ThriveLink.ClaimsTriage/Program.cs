// Claims triage job: runs on a schedule (Azure Container Apps Job), triages new claims, then exits.
// AI review is optional: without Anthropic:ApiKey the rule checks still run.
using Anthropic;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ThriveLink.ClaimsTriage;
using ThriveLink.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<ThriveLinkDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Sql"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton(TimeProvider.System);

var apiKey = builder.Configuration["Anthropic:ApiKey"];
if (!string.IsNullOrWhiteSpace(apiKey))
    builder.Services.AddSingleton<IClaimAssistant>(new AnthropicClaimAssistant(new AnthropicClient { ApiKey = apiKey }));

builder.Services.AddScoped(sp => new ClaimTriageService(
    sp.GetRequiredService<ThriveLinkDbContext>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetService<IClaimAssistant>()));

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ClaimsTriage");
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));

using var scope = host.Services.CreateScope();
var summary = await scope.ServiceProvider.GetRequiredService<ClaimTriageService>().TriagePendingAsync(batchSize: 50, cts.Token);
Log.RunComplete(log, summary.Triaged, summary.AiAssisted, summary.AiFailed, apiKey is { Length: > 0 });

// Non-zero exit marks the job execution as failed in Container Apps if every AI call failed.
return summary.AiFailed > 0 && summary.AiAssisted == 0 && summary.Triaged > 0 ? 1 : 0;

internal static partial class Log
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "Claims triage run: {Triaged} triaged, {AiAssisted} with AI, {AiFailed} AI failures (AI enabled: {AiEnabled})")]
    public static partial void RunComplete(ILogger logger, int triaged, int aiAssisted, int aiFailed, bool aiEnabled);
}
