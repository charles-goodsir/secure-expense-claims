
namespace ExpenseClaims.Api.Domain;

public enum ClaimStatus { Draft, Submitted, Approved, Rejected, Paid }

public class Claim
{
    public Guid Id { get; set; } =
Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public User? Employee { get; set; }

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NZD";

    public ClaimStatus Status { get; set; } = ClaimStatus.Draft;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedById { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
    // Maps to Postgres's xmin system column: two people acting on the same claim at once
    // can't both succeed, because the second save sees a changed version.

    public uint Version { get; set; }
}
