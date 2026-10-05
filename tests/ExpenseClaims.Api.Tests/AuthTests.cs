using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;


namespace ExpenseClaims.Api.Tests;

public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static HttpRequestMessage MeRequest(string? userId, string? roles)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        if (userId is not null) request.Headers.Add("X-Dev-User", userId);
        if (roles is not null) request.Headers.Add("X-Dev-Roles", roles);
        return request;
    }

    private record Me(string Id, string[] Roles);

    [Fact]
    public async Task Dev_sign_in_works_in_Development()
    {
        var userId = Guid.NewGuid().ToString();

        var response = await factory.CreateClient().SendAsync(MeRequest(userId, "Employee, Manager"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<Me>();
        Assert.Equal(userId, me!.Id);
        Assert.Equal(["Employee", "Manager"], me.Roles);
    }

    [Fact]
    public async Task Request_without_a_user_is_rejected()
    {
        var response = await factory.CreateClient().SendAsync(MeRequest(null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task Dev_sign_in_is_ignored_in_production()
    {
        var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var response = await production.CreateClient().SendAsync(MeRequest(Guid.NewGuid().ToString(), "Admin"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
