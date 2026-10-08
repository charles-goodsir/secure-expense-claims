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

    [Fact]
    public async Task Setup_creates_the_api_role_through_the_second_connection_and_is_safe_to_rerun()
    {
        await using var db = factory.NewDbContext();
        // In Azure the role function lives in a different database from the app's tables.
        await db.Database.ExecuteSqlRawAsync("CREATE DATABASE role_functions");
        var roleFunctionsDb = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Database = "role_functions",
        };
        await using var roleFunctions = NpgsqlDataSource.Create(roleFunctionsDb.ConnectionString);
        // Stands in for Azure's function, which doesn't exist outside Azure.
        await using (var fake = roleFunctions.CreateCommand("""
            CREATE FUNCTION pgaadauth_create_principal_with_oid(role text, oid text, kind text, admin bool, mfa bool)
            RETURNS text LANGUAGE plpgsql AS $$ BEGIN EXECUTE format('CREATE ROLE %I', role); RETURN 'ok'; END $$
            """))
        {
            await fake.ExecuteNonQueryAsync();
        }

        // The job runs after every apply, so a second run must not fail on the existing role.
        await DatabaseSetup.RunAsync(db, roleFunctions, "api_setup_test", "00000000-0000-0000-0000-000000000000");
        await DatabaseSetup.RunAsync(db, roleFunctions, "api_setup_test", "00000000-0000-0000-0000-000000000000");

        var roleExists = await db.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'api_setup_test') AS \"Value\"")
            .SingleAsync();
        Assert.True(roleExists);
    }
}
