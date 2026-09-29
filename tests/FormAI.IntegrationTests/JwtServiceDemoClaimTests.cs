using System.IdentityModel.Tokens.Jwt;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace FormAI.IntegrationTests;

public class JwtServiceDemoClaimTests
{
    private static readonly JwtService Service = new(Options.Create(new JwtSettings
    {
        Secret = "test-secret-that-is-at-least-32-characters-long",
        Issuer = "formai-test",
        Audience = "formai-test",
    }));

    private static JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void DemoUserTokenCarriesIsDemoTrue()
    {
        var token = Decode(Service.GenerateAccessToken(User.CreateDemo("hash")));

        Assert.Equal("true", token.Claims.Single(c => c.Type == "is_demo").Value);
    }

    [Fact]
    public void RegularUserTokenHasNoIsDemoClaim()
    {
        var token = Decode(Service.GenerateAccessToken(User.Create("Regular", "regular@example.com", "hash")));

        Assert.DoesNotContain(token.Claims, c => c.Type == "is_demo");
    }
}
