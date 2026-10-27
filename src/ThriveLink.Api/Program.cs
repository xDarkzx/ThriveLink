using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using ThriveLink.Api;
using ThriveLink.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ThriveLinkDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Sql"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ActivityIngestService>();
builder.Services.AddScoped<WeeklyRewardService>();
builder.Services.AddScoped<ClaimService>();

// Claim documents go to Blob via the app's managed identity when an endpoint is configured.
var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
builder.Services.AddSingleton<IDocumentStore>(string.IsNullOrEmpty(blobEndpoint)
    ? new UnconfiguredDocumentStore()
    : new BlobDocumentStore(new Uri(blobEndpoint)));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Application Insights reads APPLICATIONINSIGHTS_CONNECTION_STRING; only wire it up when it's configured.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

// Locally the app migrates itself. In Azure, migrations are applied by the deployment pipeline
// and the app's identity has read/write rights only, so these flags stay off there.
// `--seed-and-exit` loads synthetic demo data into whatever database is configured, then stops.
var seedAndExit = args.Contains("--seed-and-exit");
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup") || app.Configuration.GetValue<bool>("Database:Seed") || seedAndExit)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ThriveLinkDbContext>();
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        await db.Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Database:Seed") || seedAndExit)
    {
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await DemoSeeder.SeedAsync(db, clock, days: clock.GetUtcNow().DayOfYear - 1);
    }
    if (seedAndExit) return;
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapStatsEndpoints();
app.MapMemberEndpoints();
app.MapActivityEndpoints();
app.MapClaimEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
