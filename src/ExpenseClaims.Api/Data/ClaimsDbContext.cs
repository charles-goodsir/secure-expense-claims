using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Data;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

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
    }
}
