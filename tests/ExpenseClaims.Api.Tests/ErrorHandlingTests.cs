using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace ExpenseClaims.Api.Tests;

public class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Forces a real failure: the API is pointed at a database port where nothing is listening,
    // so the first query throws inside Npgsql.
    [Fact]
    public async Task Unexpected_error_returns_a_generic_problem_without_internal_details()
    {
        var broken = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Claims", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"));
        var client = broken.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Employee");

        var response = await client.GetAsync("/claims");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }
}
