using BCrypt.Net;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ecommerce.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RegisterAsync(RegisterRequestDto request)
    {
        var email = request.Email.Trim().ToLower();

        var existingUser = await _userRepository.ExistsByEmailAsync(email);

        if (existingUser)
        {
            _logger.LogWarning("Register failed: email already exists {Email}", email);
            throw new ArgumentException("Email already exists");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Email = email,
            PasswordHash = passwordHash,
            Role = "User"
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        _logger.LogInformation("New user registered {Email}", email);
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var email = request.Email.Trim().ToLower();

        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null)
        {
            _logger.LogWarning("Login failed: email not found {Email}", email);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var validPassword = BCrypt.Net.BCrypt.Verify(
            request.Password,
            user.PasswordHash
        );

        if (!validPassword)
        {
            _logger.LogWarning("Login failed: wrong password for {Email}", email);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var token = GenerateJwtToken(user);

        _logger.LogInformation("User login success {UserId}", user.Id);

        return new LoginResponseDto
        {
            Token = token,
            Id = user.Id,
            Email = user.Email,
            Role = user.Role
        };
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("Jwt");

        var keyString = jwtSettings["Key"]
            ?? throw new InvalidOperationException("JWT Key not configured");

        var issuer = jwtSettings["Issuer"]
            ?? throw new InvalidOperationException("JWT Issuer not configured");

        var audience = jwtSettings["Audience"]
            ?? throw new InvalidOperationException("JWT Audience not configured");

        var expireMinutes = jwtSettings["ExpireMinutes"]
            ?? throw new InvalidOperationException("JWT ExpireMinutes not configured");

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(keyString)
        );

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256
        );

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(expireMinutes)),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}