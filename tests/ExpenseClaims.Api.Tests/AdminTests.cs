using System.Net;
using System.Net.Http.Json;
using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Tests;

public class AdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ClaimDto(Guid Id);
    private record AuditDto(string Action, string? FromStatus, string? ToStatus, Guid ActorId);

    private async Task<Guid> AddUserAsync(Guid? managerId = null, string? bankAccount = null)
    {
        await using var db = factory.NewDbContext();
        var user = new User
        {
            DisplayName = "Test",
            Email = $"{Guid.NewGuid()}@example.com",
            ManagerId = managerId,
            BankAccountNumber = bankAccount,
        };
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

    [Fact]
    public async Task Admin_can_list_users_without_bank_details()
    {
        await AddUserAsync(bankAccount: "12-3456-7890123-00");
        var admin = ClientFor(await AddUserAsync(), "Admin");

        var response = await admin.GetAsync("/admin/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("12-3456-7890123-00", body);
        Assert.DoesNotContain("bankAccount", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_admin_cannot_use_admin_endpoints()
    {
        var manager = ClientFor(await AddUserAsync(), "Employee,Manager");

        var users = await manager.GetAsync("/admin/users");
        var audit = await manager.GetAsync("/admin/audit");

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (users.StatusCode, audit.StatusCode));
    }

    [Fact]
    public async Task Changing_a_manager_is_audited_and_moves_approval_to_the_new_manager()
    {
        var oldManagerId = await AddUserAsync();
        var newManagerId = await AddUserAsync();
        var employeeId = await AddUserAsync(oldManagerId);
        var adminId = await AddUserAsync();

        var response = await ClientFor(adminId, "Admin")
            .PutAsJsonAsync($"/admin/users/{employeeId}/manager", new { managerId = newManagerId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using (var db = factory.NewDbContext())
        {
            var audit = await db.AuditEntries.SingleAsync(a => a.Action == "SetManager" && a.Detail!.Contains(employeeId.ToString()));
            Assert.Equal(adminId, audit.ActorId);
        }

        var employee = ClientFor(employeeId, "Employee");
        var created = await employee.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 20m });
        var claimId = (await created.Content.ReadFromJsonAsync<ClaimDto>())!.Id;
        await employee.PostAsync($"/claims/{claimId}/submit", null);

        var oldManager = await ClientFor(oldManagerId, "Manager").PostAsync($"/claims/{claimId}/approve", null);
        var newManager = await ClientFor(newManagerId, "Manager").PostAsync($"/claims/{claimId}/approve", null);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (oldManager.StatusCode, newManager.StatusCode));
    }

    [Fact]
    public async Task User_cannot_be_made_their_own_manager()
    {
        var employeeId = await AddUserAsync();
        var admin = ClientFor(await AddUserAsync(), "Admin");

        var response = await admin.PutAsJsonAsync($"/admin/users/{employeeId}/manager", new { managerId = employeeId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manager_must_be_an_existing_user()
    {
        var employeeId = await AddUserAsync();
        var admin = ClientFor(await AddUserAsync(), "Admin");

        var response = await admin.PutAsJsonAsync($"/admin/users/{employeeId}/manager", new { managerId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_read_a_claims_audit_trail()
    {
        var managerId = await AddUserAsync();
        var employee = ClientFor(await AddUserAsync(managerId), "Employee");
        var created = await employee.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 20m });
        var claimId = (await created.Content.ReadFromJsonAsync<ClaimDto>())!.Id;
        await employee.PostAsync($"/claims/{claimId}/submit", null);

        var audit = await ClientFor(await AddUserAsync(), "Admin")
            .GetFromJsonAsync<AuditDto[]>($"/admin/audit?claimId={claimId}");

        Assert.Equal(["Submit"], audit!.Select(a => a.Action));
    }

    [Fact]
    public async Task Admin_cannot_approve_claims()
    {
        var managerId = await AddUserAsync();
        var employee = ClientFor(await AddUserAsync(managerId), "Employee");
        var created = await employee.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 20m });
        var claimId = (await created.Content.ReadFromJsonAsync<ClaimDto>())!.Id;
        await employee.PostAsync($"/claims/{claimId}/submit", null);

        var response = await ClientFor(await AddUserAsync(), "Admin").PostAsync($"/claims/{claimId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
