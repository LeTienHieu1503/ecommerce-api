using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

public class JwtTokenServiceTests
{
    // =============================================
    // Config giả — dùng InMemory thay vì mock
    // vì IConfiguration có nhiều nested key
    // =============================================
    private readonly IConfiguration _config;
    private readonly JwtTokenService _sut;

    public JwtTokenServiceTests()
    {
        // Tạo config giả với đầy đủ JWT settings
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]            = "super-secret-key-for-testing-only-32chars!!",
                ["Jwt:Issuer"]         = "TestIssuer",
                ["Jwt:Audience"]       = "TestAudience",
                ["Jwt:ExpireMinutes"]  = "30"
            })
            .Build();

        _sut = new JwtTokenService(_config);
    }

    // =============================================
    // Helper — tạo User giả
    // =============================================
    private static User CreateFakeUser(int id = 1, string email = "test@example.com")
        => new()
        {
            Id = id,
            Email = email,
            CurrentSessionId = "session-abc",
            SessionVersion = 1,
            LastLoginIpHash = "fakehash",
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = "User" } }
            }
        };

    // =============================================
    // GENERATETOKEN TESTS
    // =============================================

    [Fact]
    public void GenerateToken_WhenValidUser_ReturnsNonEmptyToken()
    {
        // Arrange
        var user = CreateFakeUser();

        // Act
        var token = _sut.GenerateToken(user);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateToken_WhenValidUser_TokenContainsCorrectClaims()
    {
        // Arrange
        var user = CreateFakeUser();

        // Act
        var token = _sut.GenerateToken(user);

        // Parse token để đọc claims
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — kiểm tra từng claim quan trọng
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "1");

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email && c.Value == "test@example.com");

        jwt.Claims.Should().Contain(c =>
            c.Type == "sid" && c.Value == "session-abc");

        jwt.Claims.Should().Contain(c =>
            c.Type == "sv" && c.Value == "1");

        jwt.Claims.Should().Contain(c =>
            c.Type == "iph" && c.Value == "fakehash");

        jwt.Claims.Should().Contain(c =>
            c.Type == "role" && c.Value == "User");
    }

    [Fact]
    public void GenerateToken_WhenValidUser_TokenHasCorrectIssuerAndAudience()
    {
        // Arrange
        var user = CreateFakeUser();

        // Act
        var token = _sut.GenerateToken(user);

        // Parse token
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert
        jwt.Issuer.Should().Be("TestIssuer");
        jwt.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public void GenerateToken_WhenValidUser_TokenExpiresIn30Minutes()
    {
        // Arrange
        var user = CreateFakeUser();
        var before = DateTime.UtcNow.AddMinutes(29);
        var after = DateTime.UtcNow.AddMinutes(31);

        // Act
        var token = _sut.GenerateToken(user);

        // Parse token
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — expiry phải nằm trong khoảng 29-31 phút
        jwt.ValidTo.Should().BeAfter(before);
        jwt.ValidTo.Should().BeBefore(after);
    }

    [Fact]
    public void GenerateToken_WhenUserHasMultipleRoles_TokenContainsAllRoles()
    {
        // Arrange
        var user = CreateFakeUser();
        user.UserRoles.Add(new UserRole { Role = new Role { Name = "Admin" } });

        // Act
        var token = _sut.GenerateToken(user);

        // Parse
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — phải có cả 2 role
        var roles = jwt.Claims
            .Where(c => c.Type == "role")
            .Select(c => c.Value)
            .ToList();

        roles.Should().Contain("User");
        roles.Should().Contain("Admin");
        roles.Should().HaveCount(2);
    }

    [Fact]
    public void GenerateToken_WithExplicitSessionParams_UsesProvidedValues()
    {
        // Arrange
        var user = CreateFakeUser();
        var sessionId = "custom-session-id";
        var sessionVersion = 5L;
        var ipHash = "custom-ip-hash";

        // Act
        var token = _sut.GenerateToken(user, sessionId, sessionVersion, ipHash);

        // Parse
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — phải dùng giá trị được truyền vào, không dùng giá trị từ user
        jwt.Claims.Should().Contain(c => c.Type == "sid" && c.Value == sessionId);
        jwt.Claims.Should().Contain(c => c.Type == "sv" && c.Value == "5");
        jwt.Claims.Should().Contain(c => c.Type == "iph" && c.Value == ipHash);
    }

    [Fact]
    public void GenerateToken_WhenJwtKeyMissing_ThrowsException()
    {
        // Arrange — config thiếu JWT Key
        var badConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]           = "",  // ← key rỗng
                ["Jwt:Issuer"]        = "TestIssuer",
                ["Jwt:Audience"]      = "TestAudience",
                ["Jwt:ExpireMinutes"] = "30"
            })
            .Build();

        var service = new JwtTokenService(badConfig);
        var user = CreateFakeUser();

        // Act
        var act = () => service.GenerateToken(user);

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*JWT Key*missing*");
    }

    // =============================================
    // GENERATEREFRESHTOKEN TESTS
    // =============================================

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyString()
    {
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateRefreshToken_EachCallReturnsUniqueToken()
    {
        // Act — gọi 2 lần
        var token1 = _sut.GenerateRefreshToken();
        var token2 = _sut.GenerateRefreshToken();

        // Assert — không được trùng nhau (random)
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsBase64String()
    {
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert — phải là Base64 hợp lệ
        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow();
    }

    // =============================================
    // GETPRINCIPALFROMEXPIREDTOKEN TESTS
    // =============================================

    [Fact]
    public void GetPrincipalFromExpiredToken_WhenValidExpiredToken_ReturnsPrincipal()
    {
        // Arrange — tạo token đã hết hạn nhưng valid
        var user = CreateFakeUser();
        var expiredToken = GenerateExpiredToken(user);

        // Act
        var principal = _sut.GetPrincipalFromExpiredToken(expiredToken);

        // Assert
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.Email)?.Value
            .Should().Be("test@example.com");
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_WhenTokenStillValid_ReturnsPrincipal()
    {
        // Arrange — token chưa hết hạn cũng phải đọc được
        var user = CreateFakeUser();
        var validToken = _sut.GenerateToken(user);

        // Act
        var principal = _sut.GetPrincipalFromExpiredToken(validToken);

        // Assert
        principal.Should().NotBeNull();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_WhenTokenTampered_ThrowsSecurityTokenException()
    {
        // Arrange — token bị giả mạo
        var tamperedToken = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJoYWNrZXIifQ.invalidsignature";

        // Act
        var act = () => _sut.GetPrincipalFromExpiredToken(tamperedToken);

        // Assert
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_WhenWrongIssuer_ThrowsSecurityTokenException()
    {
        // Arrange — tạo token với Issuer khác
        var wrongConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]           = "super-secret-key-for-testing-only-32chars!!",
                ["Jwt:Issuer"]        = "WrongIssuer",  // ← Issuer khác
                ["Jwt:Audience"]      = "TestAudience",
                ["Jwt:ExpireMinutes"] = "30"
            })
            .Build();

        var wrongService = new JwtTokenService(wrongConfig);
        var user = CreateFakeUser();
        var tokenWithWrongIssuer = wrongService.GenerateToken(user);

        // Act — dùng service với Issuer đúng để validate token sai Issuer
        var act = () => _sut.GetPrincipalFromExpiredToken(tokenWithWrongIssuer);

        // Assert
        act.Should().Throw<SecurityTokenException>();
    }

    // =============================================
    // Helper — tạo token đã hết hạn
    // =============================================
    private string GenerateExpiredToken(User user)
    {
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(
                "super-secret-key-for-testing-only-32chars!!"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name,           user.Email),
            new(ClaimTypes.Email,          user.Email),
            new("sid",                     user.CurrentSessionId ?? ""),
            new("sv",                      user.SessionVersion.ToString()),
            new("iph",                     user.LastLoginIpHash ?? "")
        };

        // Tạo token đã hết hạn 1 giờ trước
        var token = new JwtSecurityToken(
            issuer: "TestIssuer",
            audience: "TestAudience",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(-1), // ← hết hạn rồi
            signingCredentials: new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}