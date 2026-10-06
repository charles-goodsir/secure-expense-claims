using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ExpenseClaims.Api.Domain;

namespace ExpenseClaims.Api.Claims;

// Requests list only the fields a caller may set. Anything else in the JSON, such as
// "status" or "employeeId", is rejected with a 400 instead of silently ignored (threat T1).

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CreateClaimRequest(
    [property: Required, StringLength(500, MinimumLength = 1)] string Description,
    [property: Range(typeof(decimal), "0.01", "100000")] decimal Amount,
    [property: RegularExpression("^[A-Z]{3}$")] string Currency = "NZD");

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateClaimRequest(
    [property: Required, StringLength(500, MinimumLength = 1)] string Description,
    [property: Range(typeof(decimal), "0.01", "100000")] decimal Amount);

public record ClaimResponse(
    Guid Id, string Description, decimal Amount, string Currency,
ClaimStatus Status, DateTimeOffset CreatedAt)
{
    public static ClaimResponse From(Claim claim) =>
        new(claim.Id, claim.Description, claim.Amount, claim.Currency, claim.Status, claim.CreatedAt);
}
