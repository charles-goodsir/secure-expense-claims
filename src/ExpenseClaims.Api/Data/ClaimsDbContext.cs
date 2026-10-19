using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseClaims.Api.Data;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options, ILogger<ClaimsDbContext> log) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    // Copies each audit row to the log once it is saved, so Sentinel can read it (threat T3).
    // Only ids go in: no names, amounts or bank details.
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var audits = ChangeTracker.Entries<AuditEntry>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        var employees = ChangeTracker.Entries<Claim>().ToDictionary(e => e.Entity.Id, e => e.Entity.EmployeeId);

        var saved = await base.SaveChangesAsync(cancellationToken);

        foreach (var a in audits)
        {
            var employee = a.ClaimId is { } c && employees.TryGetValue(c, out var e) ? e.ToString() : "none";
            log.LogInformation("AUDIT action={Action} actor={Actor} claim={Claim} employee={Employee} from={From} to={To} detail={Detail}",
                a.Action, a.ActorId, a.ClaimId?.ToString() ?? "none", employee, a.FromStatus, a.ToStatus, a.Detail);
        }
        return saved;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(200);
            user.Property(u => u.Email).HasMaxLength(320);
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.BankAccountNumber).HasMaxLength(34);
            user.HasOne(u => u.Manager).WithMany().HasForeignKey(u => u.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Claim>(claim =>
        {
            claim.Property(c => c.Description).HasMaxLength(500);
            claim.Property(c => c.Amount).HasPrecision(12, 2);
            claim.Property(c => c.Currency).HasMaxLength(3).IsFixedLength();
            claim.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            claim.Property(c => c.Version).IsRowVersion();
            claim.HasOne(c => c.Employee).WithMany().HasForeignKey(c => c.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
            claim.HasIndex(c => new { c.EmployeeId, c.Status });
            claim.ToTable(t => t.HasCheckConstraint("ck_claims_amount_positive", "\"Amount\" > 0"));
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.Property(a => a.Action).HasMaxLength(50);
            audit.Property(a => a.FromStatus).HasConversion<string>().HasMaxLength(20);
            audit.Property(a => a.ToStatus).HasConversion<string>().HasMaxLength(20);
            audit.Property(a => a.Detail).HasMaxLength(1000);
            audit.HasIndex(a => a.ClaimId);
        });

        modelBuilder.Entity<Receipt>(receipt =>
        {
            receipt.Property(r => r.ContentType).HasMaxLength(50);
            receipt.Property(r => r.OriginalFileName).HasMaxLength(255);
            receipt.HasOne<Claim>().WithMany().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
