using Azure.Identity;
using Azure.Storage.Blobs;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ThriveLink.Data;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddDbContext<ThriveLinkDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Sql"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ActivityIngestService>();
builder.Services.AddScoped<WeeklyRewardService>();

// Raw payload archive. Uses the function app's managed identity in Azure; skipped if not configured locally.
var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
if (!string.IsNullOrEmpty(blobEndpoint))
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential()));

await builder.Build().RunAsync();
