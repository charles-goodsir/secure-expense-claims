using ExpenseClaims.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExpenseClaims.Api.Tests;

public class DatabaseSetupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Runs the same grants the API identity gets in Azure, then acts as that role.
    private async Task<ClaimsDbContext> ConnectAsApiRoleAsync()
    {
        var db = factory.NewDbContext();
        await db.Database.ExecuteSqlRawAsync("CREATE ROLE api_test");
        await DatabaseSetup.GrantApiAccessAsync(db, "api_test");
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE api_test");
        return db;
    }

    [Fact]
    public async Task Api_role_can_read_and_write_claims_and_add_audit_rows_but_not_change_or_delete_them()
    {
        await using var db = await ConnectAsApiRoleAsync();

        await db.Database.ExecuteSqlRawAsync("""SELECT count(*) FROM "Claims" """);
        await db.Database.ExecuteSqlRawAsync("""SELECT count(*) FROM "AuditEntries" """);
        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("""UPDATE "AuditEntries" SET "Action" = 'x' """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("""DELETE FROM "AuditEntries" """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        var alter = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Claims" ADD COLUMN "Sneaky" int"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, alter.SqlState);
    }
}
