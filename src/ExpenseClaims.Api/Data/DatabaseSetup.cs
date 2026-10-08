using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Data;

// Run in Azure by the migration job (ExpenseClaims.Api.dll --migrate), signed in as the
// migrations identity, which is the server's Entra admin. The API's identity gets only the
// rights granted here (E5).
public static class DatabaseSetup
{
    public static async Task RunAsync(ClaimsDbContext db, string apiRole, string apiObjectId)
    {
        await db.Database.MigrateAsync();
        // Azure-only function. It maps the role to the identity's object ID, which is unique,
        // where a display name might not be.
        await db.Database.ExecuteSqlAsync($"""
            SELECT pgaadauth_create_principal_with_oid({apiRole}, {apiObjectId}, 'service', false, false)
            WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = {apiRole})
            """);
        await GrantApiAccessAsync(db, apiRole);
    }

    // Read and write on the app's tables, but audit rows can only be added, never changed or
    // deleted (T3). No DDL, so the app can't change the schema. Tables are listed by name: a
    // new table gets no access until it's added here, so the app fails closed.
    public static async Task GrantApiAccessAsync(ClaimsDbContext db, string apiRole)
    {
        // Identifiers can't be query parameters, so they're quoted instead. Both come from the
        // job's own configuration, not from a request.
        static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
        var role = Quote(apiRole);
        var database = Quote(db.Database.GetDbConnection().Database);
#pragma warning disable EF1002 // Quoted above; GRANT can't take parameters.
        await db.Database.ExecuteSqlRawAsync($"""
            GRANT CONNECT ON DATABASE {database} TO {role};
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON "Users", "Claims", "Receipts" TO {role};
            GRANT SELECT, INSERT ON "AuditEntries" TO {role};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {role};
            """);
#pragma warning restore EF1002
    }
}
