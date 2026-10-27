using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using ThriveLink.Api;
using ThriveLink.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ThriveLinkDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Sql"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ActivityIngestService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Application Insights reads APPLICATIONINSIGHTS_CONNECTION_STRING; only wire it up when it's configured.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ThriveLinkDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Database:Seed"))
    {
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var daysThisYear = DateTime.UtcNow.DayOfYear - 1;
        await DemoSeeder.SeedAsync(db, clock, days: daysThisYear);
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapStatsEndpoints();
app.MapMemberEndpoints();
app.MapActivityEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
