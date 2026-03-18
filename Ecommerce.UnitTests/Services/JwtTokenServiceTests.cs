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
        var role = new Role
        {
            Id = new System.Random().Next(1, 10000),
            Name = "Admin"
        };

        var userId = new System.Random().Next(1, 10000);

        var user = new User
        {
            Id = userId,
            Email = "test@email.com"
        };

        user.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.Id,
            Role = role,
            User = user
        });

        var token = _service.GenerateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier &&
            c.Value == userId.ToString()
        );

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email &&
            c.Value == "test@email.com"
        );

        jwt.Claims.Should().Contain(c =>
            c.Type == "role" &&
            c.Value == "Admin"
        );
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnNotEmptyString()
    {
        var result = _service.GenerateRefreshToken();
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ShouldReturnPrincipal_WhenTokenIsValid()
    {
        var user = new User { Id = 1, Email = "test@email.com" };
        var token = _service.GenerateToken(user);
        
        var principal = _service.GetPrincipalFromExpiredToken(token);
        
        principal.Should().NotBeNull();
        principal.FindFirst(ClaimTypes.Email)?.Value.Should().Be("test@email.com");
    }
}
