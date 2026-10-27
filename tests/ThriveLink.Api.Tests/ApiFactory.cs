using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.MsSql;

using ThriveLink.Data;
using ThriveLink.Domain;

namespace ThriveLink.Api.Tests;

/// <summary>Runs the real API against a throwaway SQL Server container with real migrations applied.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sql", _sql.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:Seed", "false");
        builder.UseSetting("Ingest:ApiKey", IngestApiKey ?? "");
        builder.ConfigureTestServices(services => services.AddSingleton<IDocumentStore>(Documents));
    }

    /// <summary>Stands in for Blob Storage so tests can assert what was uploaded.</summary>
    public InMemoryDocumentStore Documents { get; } = new();

    /// <summary>When set, the API requires this X-Api-Key on ingest, as it does in Azure.</summary>
    protected virtual string? IngestApiKey => null;

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();

        // Resolving Services starts the host, which applies migrations; then add a known member to test against.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThriveLinkDbContext>();
        db.Members.Add(new Member
        {
            MemberNumber = "TEST001",
            FirstName = "Test",
            LastName = "Member",
            DateOfBirth = new DateOnly(1990, 1, 1),
            PolicyNumber = "POL-TEST",
            WeeklyTarget = 300,
            JoinedOn = new DateOnly(2025, 1, 1),
            Devices = [new LinkedDevice { Provider = DeviceProvider.AppleHealth, LinkedAt = DateTimeOffset.UtcNow }],
        });
        await db.SaveChangesAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
