using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Tests;

public class DataRulesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<User> AddUserAsync()
    {
        await using var db = factory.NewDbContext();
        var user = new User { DisplayName = "Test", Email = $"{Guid.NewGuid()}@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
    [Fact]
    public async Task Database_rejects_a_claim_with_a_negative_amount()
    {
        var user = await AddUserAsync();
        await using var db = factory.NewDbContext();
        db.Claims.Add(new Claim { EmployeeId = user.Id, Description = "Taxi", Amount = -5m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Second_of_two_concurrent_updates_fails()
    {
        var user = await AddUserAsync();
        Guid claimId;
        await using (var setup = factory.NewDbContext())
        {
            var claim = new Claim { EmployeeId = user.Id, Description = "Taxi", Amount = 25m };
            setup.Claims.Add(claim);
            await setup.SaveChangesAsync();
            claimId = claim.Id;

        }
        // Two people load the same claim, then both try to change it.
        await using var first = factory.NewDbContext();
        await using var second = factory.NewDbContext();
        var firstCopy = await first.Claims.SingleAsync(c => c.Id == claimId);
        var secondCopy = await second.Claims.SingleAsync(c => c.Id == claimId);

        firstCopy.Status = ClaimStatus.Submitted;
        await first.SaveChangesAsync();

        secondCopy.Amount = 2500m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
