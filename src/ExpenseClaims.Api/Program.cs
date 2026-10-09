using ExpenseClaims.Api.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseClaims.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text.Json.Serialization;
using ExpenseClaims.Api.Claims;
using Azure.Storage.Blobs;
using ExpenseClaims.Api.Receipts;
using System.Threading.RateLimiting;
using ExpenseClaims.Api.Admin;
using Azure.Core;
using Azure.Identity;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// In Azure the app signs in to Postgres and Storage as its user-assigned managed identity,
// so no password or key exists anywhere (I5). Locally this is unset and the connection
// strings carry the Compose password and the Azurite key instead.
var managedIdentityClientId = builder.Configuration["ManagedIdentity:ClientId"];
TokenCredential? azureCredential = managedIdentityClientId is null
    ? null
    : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));

builder.Services.AddDbContext<ClaimsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Claims"), npgsql =>
    {
        if (azureCredential is null) return;
        // Tokens last about an hour, so Npgsql fetches a fresh one every 55 minutes, and
        // retries after 5 seconds if that fails.
        npgsql.ConfigureDataSource(dataSource => dataSource.UsePeriodicPasswordProvider(
            GetDatabaseTokenAsync, TimeSpan.FromMinutes(55), TimeSpan.FromSeconds(5)));
    }));

// An Entra token is the Postgres password.
async ValueTask<string> GetDatabaseTokenAsync(NpgsqlConnectionStringBuilder _, CancellationToken cancellationToken) =>
    (await azureCredential!.GetTokenAsync(
        new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]),
        cancellationToken)).Token;

builder.Services.AddHealthChecks().AddDbContextCheck<ClaimsDbContext>(tags: ["ready"]);

// Development uses the stub sign-in. Every other environment takes Entra ID access tokens.
// Authority and ValidAudiences come from Authentication:Schemes:Bearer in configuration;
// with neither set, every request is rejected (threat S1).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
}
else
{
    builder.Services.AddAuthentication().AddJwtBearer(options => options.Events = new JwtBearerEvents
    {
        // The default mapping makes "sub" the user ID, but "sub" is different for every app
        // the user signs in to. "oid" is the user's one Entra object ID, and it's User.Id.
        OnTokenValidated = async context =>
        {
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            var objectId = identity.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier");
            var userName = identity.FindFirst("preferred_username");
            if (objectId is null || userName is null)
            {
                context.Fail("Token has no oid or preferred_username claim");
                return;
            }
            if (!identity.HasClaim(claim => claim.Type == ClaimTypes.Role))
            {
                context.Fail("Token has no roles.");
                return;
            }
            identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, objectId.Value));

            // Entra decides who may sign in, so a user's first valid token creates their
            // row. Managers are then set by an Admin, which is audited (threat R2).
            // ponytail: a duplicate first request at the same moment gets a 500; fine at this scale.
            var id = Guid.Parse(objectId.Value);
            var db = context.HttpContext.RequestServices.GetRequiredService<ClaimsDbContext>();
            if (!await db.Users.AnyAsync(u => u.Id == id))
            {
                db.Users.Add(new ExpenseClaims.Api.Domain.User
                {
                    Id = id,
                    DisplayName = identity.FindFirst("name")?.Value ?? userName.Value,
                    Email = userName.Value,
                });
                await db.SaveChangesAsync();
            }
        },
    });
}
builder.Services.AddAuthorization();
// Errors come back as RFC 9457 problem details: a status, a title and a trace ID to quote
// when reporting it. Never the exception, stack trace or connection details (threat I4).
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
// Enums go over the wire as names ("Draft"), the same as they're stored in the database.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));



// Receipts live in Blob Storage: Azurite locally with a connection string, Azure Storage in
// Azure with the managed identity. There the setting is the container's URL, not a secret.
builder.Services.AddSingleton(_ => azureCredential is null
    ? new BlobContainerClient(builder.Configuration.GetConnectionString("Receipts"), "receipts")
    : new BlobContainerClient(new Uri(builder.Configuration["Receipts:ContainerUri"]!), azureCredential));
// Each signed-in user gets their own budget of requests per minute, so one account can't
// flood the API for everyone else. Anonymous callers are counted by IP address (threat D2).
var requestsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 100);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var key = userId is not null ? $"user:{userId}" : $"ip:{context.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = requestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

var app = builder.Build();


// The migration job in Azure: apply migrations, set up the API's database role, then exit.
if (args.Contains("--migrate"))
{
    // Azure keeps its Entra role functions in the postgres database, so creating the API's
    // role needs a second connection there, signed in the same way.
    var postgresDatabase = new NpgsqlConnectionStringBuilder(app.Configuration.GetConnectionString("Claims"))
    {
        Database = "postgres",
    };
    await using var postgres = new NpgsqlDataSourceBuilder(postgresDatabase.ConnectionString)
        .UsePeriodicPasswordProvider(GetDatabaseTokenAsync, TimeSpan.FromMinutes(55), TimeSpan.FromSeconds(5))
        .Build();
    using var scope = app.Services.CreateScope();
    await DatabaseSetup.RunAsync(
        scope.ServiceProvider.GetRequiredService<ClaimsDbContext>(),
        postgres,
        app.Configuration["ApiRole:Name"]!,
        app.Configuration["ApiRole:ObjectId"]!);
    return;
}

// The web app calls the API under /api, as it does through the Vite and nginx proxies.
// Here nothing strips the prefix, so the API does. Paths without it still work.
app.UsePathBase("/api");

// First in the pipeline, so it catches exceptions from everything after it. Used in every
// environment, including Development, so what testers see is what production sends.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A malformed request (for example an unknown JSON field) is the caller's mistake: keep its
    // 400 instead of reporting it as a server error.
    StatusCodeSelector = exception =>
        exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});

app.UseStatusCodePages();
// In Azure the image carries the built web app in wwwroot: / serves index.html. Locally
// there's no wwwroot, and these do nothing.
app.UseDefaultFiles();
app.UseStaticFiles();
// Locally the app prepares its own dependencies on startup: database migrations, the
// receipts container and four test users. In Azure, Terraform and the deployment do this.
// Tests that break a dependency on purpose switch it off with LocalSetup:Enabled=false.

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("LocalSetup:Enabled", true))
{
    await app.Services.GetRequiredService<BlobContainerClient>().CreateIfNotExistsAsync();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.MigrateAsync();
    await DevSeed.AddMissingUsersAsync(db);
}

app.UseAuthentication();
app.UseAuthorization();
// After authentication, so the limiter knows who the caller is.
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
}).DisableRateLimiting();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).DisableRateLimiting();

// Who the API thinks the caller is. Useful for checking sign-in, and for Burp in Phase 3.
app.MapGet("/me", (ClaimsPrincipal user) => new
{
    Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
    Roles = user.FindAll(ClaimTypes.Role).Select(role => role.Value),
}).RequireAuthorization();
app.MapClaimEndpoints();
app.MapWorkflowEndpoints();
app.MapReceiptEndpoints();
app.MapAdminEndpoints();
app.Run();
