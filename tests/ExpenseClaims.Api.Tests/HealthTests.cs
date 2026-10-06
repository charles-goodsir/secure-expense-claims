using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpenseClaims.Api.Tests;

public class HealthTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_200()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
