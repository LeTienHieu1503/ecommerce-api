using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _service;

    public JwtTokenServiceTests()
    {
        var settings = new Dictionary<string, string>
        {
            {"Jwt:Key", "THIS_IS_A_SUPER_SECRET_TEST_KEY_12345678901234567890"},
            {"Jwt:Issuer", "TestIssuer"},
            {"Jwt:Audience", "TestAudience"},
            {"Jwt:ExpireMinutes", "60"}
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        _service = new JwtTokenService(config);
    }

    [Fact]
    public void GenerateToken_ShouldContainCorrectClaims()
    {
        var user = new User
        {
            Id = 1,
            Email = "test@email.com",
            Role = "Admin"
        };

        var token = _service.GenerateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier &&
            c.Value == "1"
        );

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email &&
            c.Value == "test@email.com"
        );

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role &&
            c.Value == "Admin"
        );
    }
}