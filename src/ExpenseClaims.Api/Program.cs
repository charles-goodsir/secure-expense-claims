using ExpenseClaims.Api.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseClaims.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json.Serialization;
using ExpenseClaims.Api.Claims;
using Azure.Storage.Blobs;
using ExpenseClaims.Api.Receipts;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<ClaimsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Claims")));

builder.Services.AddHealthChecks().AddDbContextCheck<ClaimsDbContext>(tags: ["ready"]);

// Development uses the stub sign-in. Every other environment uses JWT bearer tokens, which
// reject every request until Entra ID is configured in Phase 3.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
}
else
{
    builder.Services.AddAuthentication().AddJwtBearer();
}
builder.Services.AddAuthorization();
// Errors come back as RFC 9457 problem details: a status, a title and a trace ID to quote
// when reporting it. Never the exception, stack trace or connection details (threat I4).
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
// Enums go over the wire as names ("Draft"), the same as they're stored in the database.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));



// Receipts live in Blob Storage: Azurite locally, Azure Storage with managed identity in Phase 2.
builder.Services.AddSingleton(_ =>
    new BlobContainerClient(builder.Configuration.GetConnectionString("Receipts"), "receipts"));
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
// Locally the container is created on startup. In Azure, Terraform creates it.
if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<BlobContainerClient>().CreateIfNotExistsAsync();
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
app.Run();
