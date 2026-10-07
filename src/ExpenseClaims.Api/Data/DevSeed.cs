using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Data;




// Four known people for local testing, one per role. Development only: Azure gets its users
// from Entra ID in Phase 3. Roles aren't stored here; the dev sign-in supplies them.


public static class DevSeed
{
    public static readonly Guid Manny = Guid.Parse("0199f0a4-0000-7000-8000-00000000000a");
    public static readonly Guid Alice = Guid.Parse("0199f0a4-0000-7000-8000-000000000001");
    public static readonly Guid Fiona = Guid.Parse("0199f0a4-0000-7000-8000-00000000000f");
    public static readonly Guid Adam = Guid.Parse("0199f0a4-0000-7000-8000-0000000000ad");

    public static async Task AddMissingUsersAsync(ClaimsDbContext db)
    {
        User[] people =
        [
            new() { Id = Manny, DisplayName = "Manny (manager)", Email = "manny@example.com" },
            new() { Id = Alice, DisplayName = "Alice (employee)", Email = "alice@example.com", ManagerId = Manny },
            new() { Id = Fiona, DisplayName = "Fiona (finance)", Email = "fiona@example.com", ManagerId = Manny },
            new() { Id = Adam, DisplayName = "Adam (admin)", Email = "adam@example.com" },
        ];

        foreach (var person in people)
        {
            if (!await db.Users.AnyAsync(u => u.Id == person.Id || u.Email == person.Email))
            {
                db.Users.Add(person);
            }
        }
        await db.SaveChangesAsync();
    }
}
