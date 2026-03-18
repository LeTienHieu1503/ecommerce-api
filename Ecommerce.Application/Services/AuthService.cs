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
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<AuthService> _logger;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITokenBlacklistService _blacklistService;

    public AuthService(
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        ILogger<AuthService> logger,
        IRoleRepository roleRepository,
        ITokenBlacklistService blacklistService)
    {
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _roleRepository = roleRepository;
        _blacklistService = blacklistService;
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
            PasswordHash = passwordHash
        };

        var defaultRole = await _roleRepository.GetByNameAsync("User");

        if (defaultRole == null)
        {
            throw new Exception("Default role 'User' not found");
        }

        user.UserRoles.Add(new UserRole
        {
            RoleId = defaultRole.Id
        });

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

        var token = _jwtTokenService.GenerateToken(user);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userRepository.SaveChangesAsync();

        _logger.LogInformation("User login success {UserId}", user.Id);

        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            Id = user.Id,
            Email = user.Email,
            Roles = user.UserRoles
            .Select(ur => ur.Role.Name)
            .ToList()
        };
    }

    public async Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request)
    {
        var principal = _jwtTokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
        {
            throw new SecurityTokenException("Invalid access token or refresh token");
        }

        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            throw new SecurityTokenException("Invalid access token or refresh token");
        }

        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null || user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            throw new SecurityTokenException("Invalid access token or refresh token");
        }

        var newAccessToken = _jwtTokenService.GenerateToken(user);
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userRepository.SaveChangesAsync();

        // Blacklist the old access token so it cannot be reused
        var handler = new JwtSecurityTokenHandler();
        if (handler.CanReadToken(request.AccessToken))
        {
            var jwtToken = handler.ReadJwtToken(request.AccessToken);
            var expiryTime = jwtToken.ValidTo;
            var currentTime = DateTime.UtcNow;

            if (expiryTime > currentTime)
            {
                var timeSpan = expiryTime - currentTime;
                await _blacklistService.BlacklistTokenAsync(request.AccessToken, timeSpan);
            }
        }

        return new LoginResponseDto
        {
            Token = newAccessToken,
            RefreshToken = newRefreshToken,
            Id = user.Id,
            Email = user.Email,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList()
        };
    }

    public async Task LogoutAsync(string token, int userId)
    {
        // 1. Blacklist the access token in Redis (even if already expired — harmless, TTL = 0 means instant expiry)
        var handler = new JwtSecurityTokenHandler();
        if (handler.CanReadToken(token))
        {
            var jwtToken = handler.ReadJwtToken(token);
            var expiryTime = jwtToken.ValidTo;
            var currentTime = DateTime.UtcNow;

            // Only blacklist if the token still has lifetime remaining
            if (expiryTime > currentTime)
            {
                var timeSpan = expiryTime - currentTime;
                await _blacklistService.BlacklistTokenAsync(token, timeSpan);
            }
        }

        // 2. Invalidate the refresh token in DB so it cannot be reused
        var user = await _userRepository.GetByIdAsync(userId);
        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            await _userRepository.SaveChangesAsync();
        }
    }
}
