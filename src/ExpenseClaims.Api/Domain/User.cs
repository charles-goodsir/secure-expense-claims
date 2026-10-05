namespace ExpenseClaims.Api.Domain;

// Roles come from the identity provider (stubbed now, Entra ID in Phase 3), not this table.

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    // Who approves this user's claims. Null for the top of the org.

    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }
    // Only the owner and Finance may read this (threat I3).

    public string? BankAccountNumber { get; set; }
}
