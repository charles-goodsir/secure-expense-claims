using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using ExpenseClaims.Api.Claims;
using ExpenseClaims.Api.Data;
using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Admin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record SetManagerRequest([property: Required] Guid? ManagerId);
// Admins manage reporting lines and read the audit log. The Admin role has no workflow
// permissions, so an admin can't approve or pay anything (threat E4).

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("Admin"));
        admin.MapGet("/Users", ListUserAsync);
        admin.MapPut("/users/{id:guid}/manager", SetManagerAsync);
        admin.MapGet("/audit", ReadAuditAsync);
    }
    // Bank details are left out: admins manage people, not payments (threat I3).

    private static async Task<IResult> ListUserAsync(ClaimsDbContext db) =>
      Results.Ok(await db.Users
          .OrderBy(u => u.DisplayName)
          .Select(u => new { u.Id, u.DisplayName, u.Email, u.ManagerId })
          .ToListAsync());
    // Changing who approves someone's claims is itself audited, because it decides who can
    // approve their money (threat R2).

    private static async Task<IResult> SetManagerAsync(Guid id, SetManagerRequest request, ClaimsPrincipal user, ClaimsDbContext db)
    {
        var employee = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
        if (employee is null)
        {
            return Results.NotFound();
        }
        var managerId = request.ManagerId!.Value;
        if (managerId == id)
        {
            return Results.Problem("A user can't be their own manager", statusCode: StatusCodes.Status400BadRequest);
        }
        if (!await db.Users.AnyAsync(u => u.Id == managerId))
        {
            return Results.Problem("That manager doesn't exist", statusCode: StatusCodes.Status400BadRequest);
        }
        db.AuditEntries.Add(new AuditEntry
        {
            ActorId = ClaimEndpoints.CallerId(user),
            Action = "SetManager",
            Detail = $"user {id}: manager {employee.ManagerId?.ToString() ?? "none"} -> {managerId}",
        });
        employee.ManagerId = managerId;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
    private static async Task<IResult> ReadAuditAsync(ClaimsDbContext db, Guid? claimId) =>
        Results.Ok(await db.AuditEntries
            .Where(a => claimId == null || a.ClaimId == claimId)
            .OrderByDescending(a => a.Id)
            .Take(200)
            .ToListAsync()
        );

}

