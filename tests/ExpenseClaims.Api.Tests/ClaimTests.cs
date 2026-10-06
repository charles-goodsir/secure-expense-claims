using System.Net;
using System.Net.Http.Json;
using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Tests;

// Every rule has a test for the allowed case and the denied case.
public class ClaimsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ClaimDto(Guid Id, string Description, decimal Amount, string Currency, string Status);

    private async Task<Guid> AddUserAsync()
    {
        await using var db = factory.NewDbContext();
        var user = new User { DisplayName = "Test", Email = $"{Guid.NewGuid()}@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private HttpClient ClientFor(Guid userId, string roles = "Employee")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        return client;
    }

    private static async Task<ClaimDto> CreateClaimAsync(HttpClient client, decimal amount = 42.50m)
    {
        var response = await client.PostAsJsonAsync("/claims", new { description = "Taxi", amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDto>())!;
    }

    [Fact]
    public async Task Employee_can_create_and_read_their_own_claim()
    {
        var alice = ClientFor(await AddUserAsync());

        var created = await CreateClaimAsync(alice);
        var read = await alice.GetFromJsonAsync<ClaimDto>($"/claims/{created.Id}");

        Assert.Equal(42.50m, read!.Amount);
        Assert.Equal("Draft", read.Status);
    }

    [Fact]
    public async Task Employee_gets_404_for_someone_elses_claim()
    {
        var alice = ClientFor(await AddUserAsync());
        var bob = ClientFor(await AddUserAsync());
        var bobsClaim = await CreateClaimAsync(bob);

        var response = await alice.GetAsync($"/claims/{bobsClaim.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_edit_someone_elses_claim()
    {
        var alice = ClientFor(await AddUserAsync());
        var bob = ClientFor(await AddUserAsync());
        var bobsClaim = await CreateClaimAsync(bob);

        var response = await alice.PutAsJsonAsync($"/claims/{bobsClaim.Id}", new { description = "Mine now", amount = 9999m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await bob.GetFromJsonAsync<ClaimDto>($"/claims/{bobsClaim.Id}");
        Assert.Equal(42.50m, unchanged!.Amount);
    }

    [Fact]
    public async Task List_returns_only_the_callers_claims()
    {
        var alice = ClientFor(await AddUserAsync());
        var bob = ClientFor(await AddUserAsync());
        var alicesClaim = await CreateClaimAsync(alice);
        await CreateClaimAsync(bob);

        var list = await alice.GetFromJsonAsync<ClaimDto[]>("/claims");

        Assert.Equal([alicesClaim.Id], list!.Select(c => c.Id));
    }

    [Fact]
    public async Task Request_that_sets_status_is_rejected()
    {
        var alice = ClientFor(await AddUserAsync());

        var response = await alice.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 10m, status = "Approved" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submitted_claim_cannot_be_edited()
    {
        var alice = ClientFor(await AddUserAsync());
        var claim = await CreateClaimAsync(alice);
        await using (var db = factory.NewDbContext())
        {
            await db.Claims.Where(c => c.Id == claim.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, ClaimStatus.Submitted));
        }

        var response = await alice.PutAsJsonAsync($"/claims/{claim.Id}", new { description = "Taxi", amount = 4250m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100000.01)]
    public async Task Amount_outside_the_allowed_range_is_rejected(decimal amount)
    {
        var alice = ClientFor(await AddUserAsync());

        var response = await alice.PostAsJsonAsync("/claims", new { description = "Taxi", amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Caller_without_the_Employee_role_is_forbidden()
    {
        var finance = ClientFor(await AddUserAsync(), roles: "Finance");

        var response = await finance.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 10m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Caller_who_is_not_a_known_user_is_forbidden()
    {
        var stranger = ClientFor(Guid.NewGuid());

        var response = await stranger.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 10m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_without_sign_in_is_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/claims");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
