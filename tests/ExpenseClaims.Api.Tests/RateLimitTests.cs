using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace ExpenseClaims.Api.Tests;

public class RateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task User_over_the_limit_gets_429_without_affecting_other_users()
    {
        // The real limit is 100 a minute. Lowered to 3 so the test doesn't send 101 requests.
        var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimit:RequestsPerMinute", "3"));
        HttpClient ClientFor(Guid userId)
        {
            var client = limited.CreateClient();
            client.DefaultRequestHeaders.Add("X-Dev-User", userId.ToString());
            client.DefaultRequestHeaders.Add("X-Dev-Roles", "Employee");
            return client;
        }
        var flooder = ClientFor(Guid.NewGuid());
        var bystander = ClientFor(Guid.NewGuid());

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await flooder.GetAsync("/claims")).StatusCode);
        }
        var bystanderStatus = (await bystander.GetAsync("/claims")).StatusCode;

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, bystanderStatus);
    }
}
