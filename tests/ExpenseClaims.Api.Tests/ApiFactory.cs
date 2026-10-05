using ExpenseClaims.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ExpenseClaims.Api.Tests;


// Starts a throwaway Postgres in Docker, points the API at it and applies the migrations.
// One container per test class that uses this fixture.

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Claims", _db.GetConnectionString());
    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => _db.DisposeAsync().AsTask();

    public ClaimsDbContext NewDbContext() =>

Services.CreateScope().ServiceProvider.GetRequiredService<ClaimsDbContext>();
}
