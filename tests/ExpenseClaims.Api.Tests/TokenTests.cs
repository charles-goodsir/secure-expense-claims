using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseClaims.Api.Tests;

// Threat S1: the API only accepts tokens Entra issued for this API, unexpired and signed.
// A test key stands in for Entra's signing key, so no network is needed.
public class TokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Issuer = "https://login.microsoftonline.com/test-tenant/v2.0";
    private const string Audience = "test-api-client-id";
    private static readonly RsaSecurityKey EntraKey = new(RSA.Create(2048));

    private HttpClient Production() => factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", Audience);
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.IssuerSigningKey = EntraKey;
            }));
    }).CreateClient();

    private static string Token(
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? key = null,
        bool includeOid = true,
        string oid = "0199f0a4-0000-7000-8000-000000000001")
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "pairwise-subject-not-the-user-id",
            ["roles"] = new[] { "Employee", "Manager" },
        };
        if (includeOid) claims["oid"] = oid;
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = expiry.AddHours(-2),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(key ?? EntraKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private async Task<HttpResponseMessage> Me(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Production().SendAsync(request);
    }

    private record MeResponse(string Id, string[] Roles);

    [Fact]
    public async Task Valid_token_signs_in_as_the_oid_with_its_roles()
    {
        var response = await Me(Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal("0199f0a4-0000-7000-8000-000000000001", me!.Id);
        Assert.Equal(["Employee", "Manager"], me.Roles);
    }

    [Fact]
    public async Task Token_for_another_app_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Me(Token(audience: "some-other-app"))).StatusCode);

    [Fact]
    public async Task Token_from_another_tenant_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Me(Token(issuer: "https://login.microsoftonline.com/other-tenant/v2.0"))).StatusCode);

    // Expired by more than the default 5 minutes of allowed clock skew.
    [Fact]
    public async Task Expired_token_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Me(Token(expires: DateTime.UtcNow.AddMinutes(-10)))).StatusCode);

    [Fact]
    public async Task Token_signed_by_another_key_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Me(Token(key: new RsaSecurityKey(RSA.Create(2048))))).StatusCode);

    // Changing the payload of a signed token breaks the signature.
    [Fact]
    public async Task Token_with_edited_roles_is_rejected()
    {
        var parts = Token().Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("\"Manager\"", "\"Finance\"");
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await Me(tampered)).StatusCode);
    }

    // alg "none": the classic JWT attack, a token with no signature at all.
    [Fact]
    public async Task Unsigned_token_is_rejected()
    {
        var parts = Token().Split('.');
        var unsigned = $"{Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.";

        Assert.Equal(HttpStatusCode.Unauthorized, (await Me(unsigned)).StatusCode);
    }

    [Fact]
    public async Task Token_without_an_oid_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Me(Token(includeOid: false))).StatusCode);
}
