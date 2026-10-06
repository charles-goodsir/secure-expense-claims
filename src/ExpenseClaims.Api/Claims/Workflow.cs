using System.Security.Claims;
using ExpenseClaims.Api.Data;
using ExpenseClaims.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Claim = ExpenseClaims.Api.Domain.Claim;

namespace ExpenseClaims.Api.Claims;

// Approve, reject and pay. Each rule is in the query that finds the claim, so a claim the
// caller isn't allowed to act on is never loaded and returns 404 (threats E1, E2, E3).

public static class Workflow
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var managers = app.MapGroup("/").RequireAuthorization(policy => policy.RequireRole("Manager"));
        managers.MapGet("/approvals", ListForApprovalAsync);
        managers.MapPost("/claims/{id:guid}/approve", ApproveAsync);
        managers.MapPost("/claims/{id:guid}/reject", RejectAsync);

        var finance = app.MapGroup("/").RequireAuthorization(policy => policy.RequireRole("Finance"));
        finance.MapGet("/payments", ListForPaymentAsync);
        finance.MapPost("/claims/{id:guid}/pay", PayAsync);


    }
    // A manager sees claims from their direct reports only, never their own.
    private static IQueryable<Claim> ManagedBy(this IQueryable<Claim> claims, Guid managerId) =>
        claims.Where(c => c.Employee!.ManagerId == managerId && c.EmployeeId != managerId);

    // Finance sees approved and paid claims across the org, never their own.

    private static IQueryable<Claim> PayableBy(this IQueryable<Claim> claims, Guid financeId) =>
        claims.Where(c => (c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid) && c.EmployeeId != financeId);

    private static async Task<IResult> ListForApprovalAsync(ClaimsPrincipal user, ClaimsDbContext db)
    {
        var waiting = await
    db.Claims.ManagedBy(ClaimEndpoints.CallerId(user))
        .Where(c => c.Status == ClaimStatus.Submitted)
        .OrderBy(c => c.SubmittedAt)
        .ToListAsync();
        return Results.Ok(waiting.Select(ClaimResponse.From));
    }

    private static Task<IResult> ApproveAsync(Guid id, ClaimsPrincipal user, ClaimsDbContext db) =>
        DecideAsync(id, user, db, ClaimStatus.Approved, "Approve");

    private static Task<IResult> RejectAsync(Guid id, ClaimsPrincipal user, ClaimsDbContext db) =>
        DecideAsync(id, user, db, ClaimStatus.Rejected, "Reject");

    private static async Task<IResult> DecideAsync(
        Guid id, ClaimsPrincipal user, ClaimsDbContext db, ClaimStatus decision, string action)
    {
        var callerId = ClaimEndpoints.CallerId(user);
        var claim = await db.Claims.ManagedBy(callerId).SingleOrDefaultAsync(c => c.Id == id);
        if (claim is null)
        {
            return Results.NotFound();
        }
        if (claim.Status != ClaimStatus.Submitted)
        {
            return Results.Conflict();
        }
        claim.DecidedAt = DateTimeOffset.UtcNow;
        claim.DecidedById = callerId;
        return await MoveAsync(claim, decision, callerId, action, db);
    }

    private static async Task<IResult> ListForPaymentAsync(ClaimsPrincipal user, ClaimsDbContext db)
    {
        var approved = await db.Claims.PayableBy(ClaimEndpoints.CallerId(user))
            .Where(c => c.Status == ClaimStatus.Approved)
            .OrderBy(c => c.DecidedAt)
            .ToListAsync();
        return Results.Ok(approved.Select(ClaimResponse.From));
    }
    private static async Task<IResult> PayAsync(Guid id, ClaimsPrincipal user, ClaimsDbContext db)
    {
        var callerId = ClaimEndpoints.CallerId(user);
        var claim = await db.Claims.PayableBy(callerId).SingleOrDefaultAsync(c => c.Id == id);
        if (claim is null)
        {
            return Results.NotFound();

        }
        if (claim.Status != ClaimStatus.Approved)
        {
            return Results.Conflict();
        }
        claim.PaidAt = DateTimeOffset.UtcNow;
        return await MoveAsync(claim, ClaimStatus.Paid, callerId, "Pay", db);
    }
    // Changes the status and writes the audit row in one SaveChanges call. EF wraps that in a
    // single database transaction, so a status change can't be saved without its audit row (T3).

    internal static async Task<IResult> MoveAsync(Claim claim, ClaimStatus to, Guid actorId, string action, ClaimsDbContext db)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            ActorId = actorId,
            Action = action,
            ClaimId = claim.Id,
            FromStatus = claim.Status,
            ToStatus = to,
        });
        claim.Status = to;

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




