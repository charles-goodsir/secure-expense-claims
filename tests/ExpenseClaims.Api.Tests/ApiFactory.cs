using ExpenseClaims.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

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

    public ConcurrentQueue<string> LogLines { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Claims", _db.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Receipts", _blobs.GetConnectionString());
        builder.UseSetting("Receipts:RequireScan", "false");
        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(LogLines)));
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

    internal sealed class CapturingLoggerProvider(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(lines);
        public void Dispose() { }

        private sealed class Capture(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, exception));
        }
    }
}

