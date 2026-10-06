using System.Security.Claims;
using ExpenseClaims.Api.Data;
using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Claim = ExpenseClaims.Api.Domain.Claim;

namespace ExpenseClaims.Api.Claims;

public static class ClaimEndpoints
{
    public static void MapClaimEndpoints(this WebApplication app)
    {
        var claims = app.MapGroup("/claims").RequireAuthorization(policy => policy.RequireRole("Employee"));

        claims.MapPost("/", CreateAsync);
        claims.MapGet("/", ListMineAsync);
        claims.MapGet("/{id:guid}", GetAsync);
        claims.MapPut("/{id:guid}", UpdateAsync);
    }

    private static Guid CallerId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> CreateAsync(CreateClaimRequest request, ClaimsPrincipal user, ClaimsDbContext db)
    {
        var callerId = CallerId(user);
        if (!await db.Users.AnyAsync(u => u.Id == callerId))
        {
            return Results.Forbid();
        }

        var claim = new Claim
        {
            EmployeeId = callerId,
            Description = request.Description,
            Amount = request.Amount,
            Currency = request.Currency,
        };
        db.Claims.Add(claim);
        await db.SaveChangesAsync();
        return Results.Created($"/claims/{claim.Id}", ClaimResponse.From(claim));
    }

    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ClaimsDbContext db)
    {
        var callerId = CallerId(user);
        var mine = await db.Claims
            .Where(c => c.EmployeeId == callerId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return Results.Ok(mine.Select(ClaimResponse.From));
    }

    // Someone else's claim returns 404, the same as a claim that doesn't exist,
    // so IDs can't be probed (threat I1).
    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ClaimsDbContext db)
    {
        var callerId = CallerId(user);
        var claim = await db.Claims.SingleOrDefaultAsync(c => c.Id == id && c.EmployeeId == callerId);
        return claim is null ? Results.NotFound() : Results.Ok(ClaimResponse.From(claim));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateClaimRequest request, ClaimsPrincipal user, ClaimsDbContext db)
    {
        var callerId = CallerId(user);
        var claim = await db.Claims.SingleOrDefaultAsync(c => c.Id == id && c.EmployeeId == callerId);
        if (claim is null)
        {
            return Results.NotFound();
        }

        // Only drafts can be edited. Once submitted, the amount is what the manager approves.
        if (claim.Status != ClaimStatus.Draft)
        {
            return Results.Conflict();
        }

        claim.Description = request.Description;
        claim.Amount = request.Amount;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict();
        }
        return Results.Ok(ClaimResponse.From(claim));
    }
}
