using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _repoMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly Mock<IJwtTokenService> _jwtMock;

    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _repoMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<AuthService>>();
        _jwtMock = new Mock<IJwtTokenService>();

        _service = new AuthService(
            _repoMock.Object,
            _jwtMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateUser_WhenEmailNotExists()
    {
        var request = new RegisterRequestDto
        {
            Email = "Test@Email.com",
            Password = "123456"
        };

        _repoMock
            .Setup(r => r.ExistsByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        await _service.RegisterAsync(request);

        _repoMock.Verify(r =>
            r.AddAsync(It.Is<User>(u =>
                u.Email == "test@email.com" &&
                u.Role == "User"
            )), Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowException_WhenEmailExists()
    {
        var request = new RegisterRequestDto
        {
            Email = "test@email.com",
            Password = "123456"
        };

        _repoMock
            .Setup(r => r.ExistsByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        Func<Task> act = async () =>
            await _service.RegisterAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsValid()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        var user = new User
        {
            Id = 1,
            Email = "test@email.com",
            PasswordHash = passwordHash,
            Role = "User"
        };

        var request = new LoginRequestDto
        {
            Email = "test@email.com",
            Password = "123456"
        };

        _repoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _jwtMock
            .Setup(j => j.GenerateToken(It.IsAny<User>()))
            .Returns("fake-jwt-token");

        var result = await _service.LoginAsync(request);

        result.Token.Should().Be("fake-jwt-token");
        result.Email.Should().Be("test@email.com");
        result.Id.Should().Be(1);
        result.Role.Should().Be("User");

        _jwtMock.Verify(j => j.GenerateToken(It.Is<User>(u => u.Id == 1)), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowException_WhenPasswordWrong()
    {
        var user = new User
        {
            Id = 1,
            Email = "test@email.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            Role = "User"
        };

        var request = new LoginRequestDto
        {
            Email = "test@email.com",
            Password = "wrong_password"
        };

        _repoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        Func<Task> act = async () =>
            await _service.LoginAsync(request);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowException_WhenUserNotFound()
    {
        var request = new LoginRequestDto
        {
            Email = "notfound@email.com",
            Password = "123456"
        };

        _repoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null);

        Func<Task> act = async () =>
            await _service.LoginAsync(request);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}