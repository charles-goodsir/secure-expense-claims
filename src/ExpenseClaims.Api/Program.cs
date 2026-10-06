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
builder.Services.AddValidation();
// Enums go over the wire as names ("Draft"), the same as they're stored in the database.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));



// Receipts live in Blob Storage: Azurite locally, Azure Storage with managed identity in Phase 2.
builder.Services.AddSingleton(_ =>
    new BlobContainerClient(builder.Configuration.GetConnectionString("Receipts"), "receipts"));

var app = builder.Build();

// Locally the container is created on startup. In Azure, Terraform creates it.
if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<BlobContainerClient>().CreateIfNotExistsAsync();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

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
