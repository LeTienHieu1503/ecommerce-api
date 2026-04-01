using Ecommerce.Application.Common.Http;
using Ecommerce.Application.Common.Security;
using Ecommerce.Application.Common.Logging;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ecommerce.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<AuthService> _logger;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITokenBlacklistService _blacklistService;
    private readonly IConfiguration _configuration;
    private readonly ICacheService? _cacheService;
    private readonly IDeviceBindingValidationService _deviceBindingValidation;
    private readonly IDeviceSessionService _deviceSessionService;
    private readonly IRequestDeviceContext _requestDeviceContext;

    private static string SessionCacheKey(int userId) => $"auth:session:user:{userId}";

    public AuthService(
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        ILogger<AuthService> logger,
        IRoleRepository roleRepository,
        ITokenBlacklistService blacklistService,
        IConfiguration configuration,
        ICacheService cacheService,
        IDeviceBindingValidationService deviceBindingValidation,
        IDeviceSessionService deviceSessionService,
        IRequestDeviceContext requestDeviceContext)
    {
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _roleRepository = roleRepository;
        _blacklistService = blacklistService;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _cacheService = cacheService;
        _deviceBindingValidation = deviceBindingValidation;
        _deviceSessionService = deviceSessionService;
        _requestDeviceContext = requestDeviceContext;
    }

    public Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
        => LoginAsync(request, "unknown", null);

    public Task<LoginResponseDto> LoginAsync(LoginRequestDto request, string clientIp)
        => LoginAsync(request, clientIp, null);

    public async Task RegisterAsync(RegisterRequestDto request)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(AuthService), nameof(RegisterAsync));
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
            throw new BusinessException("Default role 'User' not found");
        }

        user.UserRoles.Add(new UserRole
        {
            RoleId = defaultRole.Id
        });

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        _logger.LogInformation("New user registered {Email}", email);
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request, string clientIp, string? userAgent)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(AuthService), nameof(LoginAsync));
        var email = request.Email.Trim().ToLower();
        var loginAttemptKey = $"auth:login:attempts:{email}";
        long attempts;
        try
        {
            attempts = await _cacheService.IncrementAsync(loginAttemptKey, TimeSpan.FromMinutes(15));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not increment login attempts in cache for {Email}", email);
            attempts = 1;
        }

        if (attempts > 5)
            throw new TooManyRequestsException("Too many login attempts. Try again later.");

        var user = await _userRepository.GetByEmailAsync(email);
        if (user == null)
            throw new UnauthorizedException("Invalid credentials");

        var validPassword = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!validPassword)
            throw new UnauthorizedException("Invalid credentials");

        // Login thành công, reset số lần thử sai
        await _cacheService.RemoveAsync(loginAttemptKey);

        var fingerprintSecret = _configuration["AuthSecurity:FingerprintSecret"] ?? "fallback-secret";
        var ipHash = IpBindingHelper.ComputeIpHash(clientIp, fingerprintSecret);

        var deviceBindingSecret = _configuration["AuthSecurity:DeviceBindingSecret"] ?? "fallback-device-secret";
        string? deviceHash = null;
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
            deviceHash = DeviceBindingHelper.ComputeDeviceHash(request.DeviceId, deviceBindingSecret);

        user.CurrentSessionId = Guid.NewGuid().ToString("N");
        user.SessionVersion += 1;
        user.LastLoginIpHash = ipHash;
        user.LastLoginDeviceHash = deviceHash;

        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

        await _userRepository.SaveChangesAsync();

        var token = _jwtTokenService.GenerateToken(user, user.CurrentSessionId, user.SessionVersion, ipHash, deviceHash);

        if (_cacheService != null)
        {
            try
            {
                await _cacheService.SetAsync(
                    SessionCacheKey(user.Id),
                    new UserSessionState
                    {
                        SessionId = user.CurrentSessionId!,
                        SessionVersion = user.SessionVersion,
                        IpHash = ipHash,
                        DeviceBindingHash = deviceHash,
                        UpdatedAt = DateTime.UtcNow
                    },
                    TimeSpan.FromDays(7));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache session state for user {UserId}", user.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(deviceHash))
        {
            try
            {
                await _deviceSessionService.RegisterAsync(
                    user.Id,
                    user.CurrentSessionId!,
                    deviceHash,
                    userAgent ?? string.Empty,
                    clientIp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Device session registry failed for user {UserId}", user.Id);
            }
        }

        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            Id = user.Id,
            Email = user.Email,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList()
        };
    }

    public Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request)
        => RefreshTokenAsync(request, "unknown", null);

    public Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request, string clientIp)
        => RefreshTokenAsync(request, clientIp, null);

    public async Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request, string clientIp, string? deviceId)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(AuthService), nameof(RefreshTokenAsync));
        var principal = _jwtTokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
            throw new SecurityTokenException("Invalid access token or refresh token");

        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
            throw new SecurityTokenException("Invalid access token or refresh token");

        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null || user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            throw new SecurityTokenException("Invalid access token or refresh token");

        var sid = principal.FindFirst("sid")?.Value;
        var svRaw = principal.FindFirst("sv")?.Value;
        var tokenIpHash = principal.FindFirst("iph")?.Value;
        if (string.IsNullOrWhiteSpace(sid) ||
            !long.TryParse(svRaw, out var sv) ||
            string.IsNullOrWhiteSpace(tokenIpHash))
        {
            throw new SecurityTokenException("Invalid token claims");
        }

        UserSessionState? session = null;
        var sessionLoadedFromRedis = false;
        if (_cacheService != null)
        {
            try
            {
                session = await _cacheService.GetAsync<UserSessionState>(SessionCacheKey(user.Id));
                sessionLoadedFromRedis = session != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read session state cache for user {UserId}", user.Id);
            }
        }

        if (session == null)
        {
            if (string.IsNullOrWhiteSpace(user.CurrentSessionId) || string.IsNullOrWhiteSpace(user.LastLoginIpHash))
                throw new SecurityTokenException("Session not initialized");

            session = new UserSessionState
            {
                SessionId = user.CurrentSessionId,
                SessionVersion = user.SessionVersion,
                IpHash = user.LastLoginIpHash,
                DeviceBindingHash = user.LastLoginDeviceHash,
                UpdatedAt = DateTime.UtcNow
            };

            if (_cacheService != null)
            {
                try
                {
                    await _cacheService.SetAsync(
                        SessionCacheKey(user.Id),
                        session,
                        TimeSpan.FromDays(7));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh session cache for user {UserId}", user.Id);
                }
            }
        }

        var fingerprintSecret = _configuration["AuthSecurity:FingerprintSecret"] ?? "fallback-secret";
        var currentIpHash = IpBindingHelper.ComputeIpHash(clientIp, fingerprintSecret);

        if (session.SessionId != sid ||
            session.SessionVersion != sv ||
            session.IpHash != tokenIpHash ||
            session.IpHash != currentIpHash)
        {
            throw new SecurityTokenException("Session invalidated");
        }

        await _deviceBindingValidation.ValidateAsync(
            deviceId,
            principal.FindFirst("dbh")?.Value,
            sessionLoadedFromRedis,
            session,
            user);

        var newAccessToken = _jwtTokenService.GenerateToken(
            user,
            session.SessionId,
            session.SessionVersion,
            session.IpHash,
            session.DeviceBindingHash);

        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

        // XÓA cache session cũ trước khi đổi refresh token để tránh conflict và đảm bảo session "rotate"
        if (_cacheService != null)
        {
            try
            {
                await _cacheService.RemoveAsync(SessionCacheKey(user.Id));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove session cache for user {UserId}", user.Id);
            }
        }

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userRepository.SaveChangesAsync();

        // Đưa token cũ vào blacklist để không thể sử dụng lại
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
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(AuthService), nameof(LogoutAsync));
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

        // 2. Invalidate refresh token and current session
        var user = await _userRepository.GetByIdAsync(userId);
        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            user.CurrentSessionId = null;
            user.LastLoginIpHash = null;
            user.LastLoginDeviceHash = null;
            user.SessionVersion += 1;
            await _userRepository.SaveChangesAsync();
        }

        if (_cacheService != null)
        {
            try
            {
                await _cacheService.RemoveAsync(SessionCacheKey(userId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove session cache for user {UserId}", userId);
            }
        }
    }
}
