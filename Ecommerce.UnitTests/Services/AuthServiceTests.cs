using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;

public class AuthServiceTests
{
    // =============================================
    // Dependencies được mock — không dùng thật
    // =============================================
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRoleRepository> _roleRepo = new();
    private readonly Mock<IJwtTokenService> _jwtService = new();
    private readonly Mock<ITokenBlacklistService> _blacklistService = new();
    private readonly Mock<ICacheService> _cacheService = new();
    private readonly Mock<ILogger<AuthService>> _logger = new();
    private readonly Mock<IConfiguration> _config = new();

    // =============================================
    // Service thật — inject mock vào
    // =============================================
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        // Setup config mock
        _config.Setup(c => c["AuthSecurity:FingerprintSecret"])
            .Returns("test-secret");

        // Tạo AuthService với tất cả dependencies là mock
        _sut = new AuthService(
            _userRepo.Object,
            _jwtService.Object,
            _logger.Object,
            _roleRepo.Object,
            _blacklistService.Object,
            _config.Object,
            _cacheService.Object);
    }

    // =============================================
    // Helper — tạo User giả để dùng trong nhiều test
    // =============================================
    private static User CreateFakeUser(int id = 1, string email = "test@example.com")
    {
        return new User
        {
            Id = id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            CurrentSessionId = Guid.NewGuid().ToString("N"),
            SessionVersion = 1,
            LastLoginIpHash = "fakehash",
            RefreshToken = "valid-refresh-token",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = "User" } }
            }
        };
    }

    // =============================================
    // REGISTER TESTS
    // =============================================

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ThrowsArgumentException()
    {
        // Arrange
        _userRepo.Setup(r => r.ExistsByEmailAsync("test@example.com"))
            .ReturnsAsync(true);

        var request = new RegisterRequestDto
        {
            Email = "test@example.com",
            Password = "123456"
        };

        // Act
        var act = () => _sut.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task RegisterAsync_WhenDefaultRoleNotFound_ThrowsException()
    {
        // Arrange
        _userRepo.Setup(r => r.ExistsByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _roleRepo.Setup(r => r.GetByNameAsync("User"))
            .ReturnsAsync((Role?)null); // Role không tồn tại

        var request = new RegisterRequestDto
        {
            Email = "new@example.com",
            Password = "123456"
        };

        // Act
        var act = () => _sut.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*Default role*");
    }

    [Fact]
    public async Task RegisterAsync_WhenValidRequest_SavesUserSuccessfully()
    {
        // Arrange
        _userRepo.Setup(r => r.ExistsByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _roleRepo.Setup(r => r.GetByNameAsync("User"))
            .ReturnsAsync(new Role { Id = 1, Name = "User" });

        _userRepo.Setup(r => r.AddAsync(It.IsAny<User>()))
            .Returns(Task.CompletedTask);

        _userRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        var request = new RegisterRequestDto
        {
            Email = "new@example.com",
            Password = "123456"
        };

        // Act
        var act = () => _sut.RegisterAsync(request);

        // Assert — không throw exception là thành công
        await act.Should().NotThrowAsync();

        // Verify AddAsync được gọi đúng 1 lần
        _userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // =============================================
    // LOGIN TESTS
    // =============================================

    [Fact]
    public async Task LoginAsync_WhenUserNotFound_ThrowsUnauthorizedException()
    {
        // Arrange
        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        var request = new LoginRequestDto
        {
            Email = "notexist@example.com",
            Password = "123456"
        };

        // Act
        var act = () => _sut.LoginAsync(request, "127.0.0.1");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Invalid credentials*");
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordWrong_ThrowsUnauthorizedException()
    {
        // Arrange
        var user = CreateFakeUser();
        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        var request = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "wrong-password" // ← sai password
        };

        // Act
        var act = () => _sut.LoginAsync(request, "127.0.0.1");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Invalid credentials*");
    }

    [Fact]
    public async Task LoginAsync_WhenValidCredentials_ReturnsTokenResponse()
    {
        // Arrange
        var user = CreateFakeUser();

        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _userRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        _jwtService.Setup(j => j.GenerateToken(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Returns("fake-jwt-token");

        _jwtService.Setup(j => j.GenerateRefreshToken())
            .Returns("fake-refresh-token");

        _cacheService.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<UserSessionState>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        var request = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "123456"
        };

        // Act
        var result = await _sut.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be("fake-jwt-token");
        result.RefreshToken.Should().Be("fake-refresh-token");
        result.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task LoginAsync_WithDeviceId_SetsDeviceHashInSession()
    {
        // Arrange
        var user = CreateFakeUser();

        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);
        _userRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        _jwtService.Setup(j => j.GenerateToken(
                It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns("fake-jwt-token");
        _jwtService.Setup(j => j.GenerateRefreshToken())
            .Returns("fake-refresh-token");

        UserSessionState? capturedSession = null;
        _cacheService.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<UserSessionState>(),
                It.IsAny<TimeSpan?>()))
            .Callback<string, UserSessionState, TimeSpan?>((_, s, _) => capturedSession = s)
            .Returns(Task.CompletedTask);

        var request = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "123456",
            DeviceId = "browser-uuid-1234"
        };

        // Act
        await _sut.LoginAsync(request, "127.0.0.1");

        // Assert — session phải có DeviceBindingHash khi có DeviceId
        capturedSession.Should().NotBeNull();
        capturedSession!.DeviceBindingHash.Should().NotBeNullOrWhiteSpace();
        user.LastLoginDeviceHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoginAsync_WithoutDeviceId_DeviceHashIsNull()
    {
        // Arrange
        var user = CreateFakeUser();

        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);
        _userRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _jwtService.Setup(j => j.GenerateToken(
                It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns("fake-jwt-token");
        _jwtService.Setup(j => j.GenerateRefreshToken())
            .Returns("fake-refresh-token");

        UserSessionState? capturedSession = null;
        _cacheService.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<UserSessionState>(),
                It.IsAny<TimeSpan?>()))
            .Callback<string, UserSessionState, TimeSpan?>((_, s, _) => capturedSession = s)
            .Returns(Task.CompletedTask);

        var request = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "123456"
            // DeviceId = null → backward-compatible
        };

        // Act
        await _sut.LoginAsync(request, "127.0.0.1");

        // Assert — không có DeviceId → DeviceBindingHash phải là null
        capturedSession.Should().NotBeNull();
        capturedSession!.DeviceBindingHash.Should().BeNull();
        user.LastLoginDeviceHash.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WhenCacheServiceFails_StillReturnsToken()
    {
        // Arrange — Cache throw exception nhưng login vẫn phải thành công
        var user = CreateFakeUser();

        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);
        _userRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _jwtService.Setup(j => j.GenerateToken(
                It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns("fake-jwt-token");
        _jwtService.Setup(j => j.GenerateRefreshToken())
            .Returns("fake-refresh-token");

        // Cache bị lỗi
        _cacheService.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<UserSessionState>(),
                It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new Exception("Redis down"));

        var request = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "123456"
        };

        // Act
        var act = () => _sut.LoginAsync(request, "127.0.0.1");

        // Assert — Cache lỗi nhưng login KHÔNG được throw exception
        await act.Should().NotThrowAsync();
    }

    // =============================================
    // LOGOUT TESTS
    // =============================================

    [Fact]
    public async Task LogoutAsync_WhenValidToken_BlacklistsTokenAndClearsSession()
    {
        // Arrange
        var user = CreateFakeUser();

        _userRepo.Setup(r => r.GetByIdAsync(user.Id))
            .ReturnsAsync(user);
        _userRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _blacklistService.Setup(b => b.BlacklistTokenAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);
        _cacheService.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Token giả — exp trong tương lai
        var fakeToken = GenerateFakeExpiredJwt(DateTime.UtcNow.AddMinutes(30));

        // Act
        await _sut.LogoutAsync(fakeToken, user.Id);

        // Assert — BlacklistToken phải được gọi
        _blacklistService.Verify(
            b => b.BlacklistTokenAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()),
            Times.Once);

        // Session phải bị xóa
        _cacheService.Verify(
            c => c.RemoveAsync(It.IsAny<string>()),
            Times.Once);

        // User session phải được reset
        user.RefreshToken.Should().BeNull();
        user.CurrentSessionId.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_WhenTokenAlreadyExpired_DoesNotBlacklist()
    {
        // Arrange
        var user = CreateFakeUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _userRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _cacheService.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Token đã hết hạn trong quá khứ
        var expiredToken = GenerateFakeExpiredJwt(DateTime.UtcNow.AddMinutes(-10));

        // Act
        await _sut.LogoutAsync(expiredToken, user.Id);

        // Assert — token đã hết hạn → KHÔNG blacklist
        _blacklistService.Verify(
            b => b.BlacklistTokenAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()),
            Times.Never);
    }

    // =============================================
    // Helper — tạo JWT giả với expiry tùy chỉnh
    // =============================================
    private static string GenerateFakeExpiredJwt(DateTime expiry)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("super-secret-key-for-testing-only"));

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "test",
            audience: "test",
            expires: expiry,
            signingCredentials: new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(token);
    }
}