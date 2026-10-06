using ExpenseClaims.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

namespace ExpenseClaims.Api.Tests;

// Starts throwaway Postgres and Azurite (Blob Storage) containers, points the API at them
// and applies the migrations.
// One container per test class that uses this fixture.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18-alpine").Build();

    // The Blob SDK uses a newer storage API version than Azurite knows about yet.
    private readonly AzuriteContainer _blobs = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Claims", _db.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Receipts", _blobs.GetConnectionString());
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_db.StartAsync(), _blobs.StartAsync());
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await _blobs.DisposeAsync();
    }

    public ClaimsDbContext NewDbContext() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<ClaimsDbContext>();
}
