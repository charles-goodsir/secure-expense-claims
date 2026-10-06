using System.Net;
using System.Net.Http.Json;
using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Tests;

// Each test builds its own small org: a manager, their direct report, and outsiders.
public class WorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ClaimDto(Guid Id, decimal Amount, string Status);

    private async Task<Guid> AddUserAsync(Guid? managerId = null)
    {
        await using var db = factory.NewDbContext();
        var user = new User { DisplayName = "Test", Email = $"{Guid.NewGuid()}@example.com", ManagerId = managerId };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private HttpClient ClientFor(Guid userId, string roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        return client;
    }

    private static async Task<Guid> SubmittedClaimAsync(HttpClient employee)
    {
        var created = await employee.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 42.50m });
        var claim = (await created.Content.ReadFromJsonAsync<ClaimDto>())!;
        var submitted = await employee.PostAsync($"/claims/{claim.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return claim.Id;
    }

    private async Task<List<AuditEntry>> AuditFor(Guid claimId)
    {
        await using var db = factory.NewDbContext();
        return await db.AuditEntries.Where(a => a.ClaimId == claimId).OrderBy(a => a.Id).ToListAsync();
    }

    // A manager with one direct report, plus their clients.
    private async Task<(HttpClient Manager, HttpClient Employee, Guid ManagerId, Guid EmployeeId)> TeamAsync()
    {
        var managerId = await AddUserAsync();
        var employeeId = await AddUserAsync(managerId);
        return (ClientFor(managerId, "Employee,Manager"), ClientFor(employeeId, "Employee"), managerId, employeeId);
    }

    [Fact]
    public async Task Submitting_writes_an_audit_entry()
    {
        var team = await TeamAsync();

        var claimId = await SubmittedClaimAsync(team.Employee);

        var audit = Assert.Single(await AuditFor(claimId));
        Assert.Equal(("Submit", ClaimStatus.Draft, ClaimStatus.Submitted, team.EmployeeId),
            (audit.Action, audit.FromStatus, audit.ToStatus, audit.ActorId));
    }

    [Fact]
    public async Task Claim_without_a_manager_cannot_be_submitted()
    {
        var loner = ClientFor(await AddUserAsync(), "Employee");
        var created = await loner.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 10m });
        var claim = (await created.Content.ReadFromJsonAsync<ClaimDto>())!;

        var response = await loner.PostAsync($"/claims/{claim.Id}/submit", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Manager_can_approve_a_direct_reports_claim()
    {
        var team = await TeamAsync();
        var claimId = await SubmittedClaimAsync(team.Employee);

        var response = await team.Manager.PostAsync($"/claims/{claimId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = await AuditFor(claimId);
        Assert.Equal(("Approve", team.ManagerId), (audit[^1].Action, audit[^1].ActorId));
    }

    [Fact]
    public async Task Manager_can_reject_a_direct_reports_claim()
    {
        var team = await TeamAsync();
        var claimId = await SubmittedClaimAsync(team.Employee);

        var response = await team.Manager.PostAsync($"/claims/{claimId}/reject", null);

        Assert.Equal("Rejected", (await response.Content.ReadFromJsonAsync<ClaimDto>())!.Status);
    }

    [Fact]
    public async Task Manager_cannot_approve_another_managers_report()
    {
        var team = await TeamAsync();
        var otherManager = ClientFor(await AddUserAsync(), "Employee,Manager");
        var claimId = await SubmittedClaimAsync(team.Employee);

        var response = await otherManager.PostAsync($"/claims/{claimId}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await AuditFor(claimId));
    }

    [Fact]
    public async Task Employee_cannot_approve_even_their_own_claim()
    {
        var team = await TeamAsync();
        var claimId = await SubmittedClaimAsync(team.Employee);

        var response = await team.Employee.PostAsync($"/claims/{claimId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_approve_their_own_claim()
    {
        // A user recorded as their own manager, the one way the reporting line allows it.
        var selfManagedId = await AddUserAsync();
        await using (var db = factory.NewDbContext())
        {
            await db.Users.Where(u => u.Id == selfManagedId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.ManagerId, selfManagedId));
        }
        var selfManaged = ClientFor(selfManagedId, "Employee,Manager");
        var claimId = await SubmittedClaimAsync(selfManaged);

        var response = await selfManaged.PostAsync($"/claims/{claimId}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Draft_claim_cannot_be_approved()
    {
        var team = await TeamAsync();
        var created = await team.Employee.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 10m });
        var claim = (await created.Content.ReadFromJsonAsync<ClaimDto>())!;

        var response = await team.Manager.PostAsync($"/claims/{claim.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Approvals_list_shows_only_direct_reports()
    {
        var team = await TeamAsync();
        var otherTeam = await TeamAsync();
        var mine = await SubmittedClaimAsync(team.Employee);
        await SubmittedClaimAsync(otherTeam.Employee);

        var list = await team.Manager.GetFromJsonAsync<ClaimDto[]>("/approvals");

        Assert.Equal([mine], list!.Select(c => c.Id));
    }

    [Fact]
    public async Task Finance_can_pay_an_approved_claim()
    {
        var team = await TeamAsync();
        var finance = ClientFor(await AddUserAsync(), "Finance");
        var claimId = await SubmittedClaimAsync(team.Employee);
        await team.Manager.PostAsync($"/claims/{claimId}/approve", null);

        var response = await finance.PostAsync($"/claims/{claimId}/pay", null);

        Assert.Equal("Paid", (await response.Content.ReadFromJsonAsync<ClaimDto>())!.Status);
        Assert.Equal(["Submit", "Approve", "Pay"], (await AuditFor(claimId)).Select(a => a.Action));
    }

    [Fact]
    public async Task Finance_cannot_see_or_pay_a_claim_that_is_not_approved()
    {
        var team = await TeamAsync();
        var finance = ClientFor(await AddUserAsync(), "Finance");
        var claimId = await SubmittedClaimAsync(team.Employee);

        var response = await finance.PostAsync($"/claims/{claimId}/pay", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Finance_cannot_pay_their_own_claim()
    {
        var managerId = await AddUserAsync();
        var financeId = await AddUserAsync(managerId);
        var finance = ClientFor(financeId, "Employee,Finance");
        var claimId = await SubmittedClaimAsync(finance);
        await ClientFor(managerId, "Manager").PostAsync($"/claims/{claimId}/approve", null);

        var response = await finance.PostAsync($"/claims/{claimId}/pay", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_pay()
    {
        var team = await TeamAsync();
        var claimId = await SubmittedClaimAsync(team.Employee);
        await team.Manager.PostAsync($"/claims/{claimId}/approve", null);

        var response = await team.Manager.PostAsync($"/claims/{claimId}/pay", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
