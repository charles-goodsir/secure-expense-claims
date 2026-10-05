namespace ExpenseClaims.Api.Domain;


public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public Guid ActorId { get; set; }
    public required string Action { get; set; }
    public Guid? ClaimId { get; set; }
    public ClaimStatus? FromStatus { get; set; }

    public ClaimStatus? ToStatus { get; set; }
    public string? Detail { get; set; }
}
