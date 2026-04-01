using Ecommerce.API.Authorization.Handlers;
using Ecommerce.API.Authorization.Policies;
using Ecommerce.API.Authorization.Requirements;
using Ecommerce.Application.Common.Http;
using Ecommerce.Application.Common.Security;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.API.Responses;
using Ecommerce.Domain.Enums;
using Ecommerce.Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace Ecommerce.API.Extensions;

public static class AuthExtensions
{
    public static IServiceCollection AddAuthConfig(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        var jwtKey = jwt["Key"] ?? throw new Exception("JWT Key is missing");

        // JWT Authentication
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt["Issuer"],
                    ValidAudience = jwt["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = BuildJwtBearerEvents();
            });

        // Authorization Policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole("Admin"));

            string[] permissions =
            {
                "product.create", "product.read", "product.update", "product.delete",
                "category.create", "category.read", "category.update", "category.delete",
                "order.create", "order.read", "order.update", "order.delete",
                "order.checkout", "order.refund"
            };

            foreach (var permission in permissions)
            {
                options.AddPolicy(permission, policy =>
                    policy.Requirements.Add(new PermissionRequirement(permission)));
            }
        });

        services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        return services;
    }

    // Build JwtBearerEvents
    private static JwtBearerEvents BuildJwtBearerEvents()
    {
        return new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT authentication failed: {Message}",
                    context.Exception.Message);
                return Task.CompletedTask;
            },

            OnTokenValidated = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var httpContext = context.HttpContext;
                var username = context.Principal?.Identity?.Name;

                var blacklistService = httpContext.RequestServices
                    .GetRequiredService<ITokenBlacklistService>();
                var cacheService = httpContext.RequestServices
                    .GetRequiredService<ICacheService>();
                var userRepository = httpContext.RequestServices
                    .GetRequiredService<IUserRepository>();
                var configuration = httpContext.RequestServices
                    .GetRequiredService<IConfiguration>();
                var deviceBindingValidation = httpContext.RequestServices
                    .GetRequiredService<IDeviceBindingValidationService>();
                var deviceSessionService = httpContext.RequestServices
                    .GetRequiredService<IDeviceSessionService>();

                // Kiểm tra blacklist
                if (context.SecurityToken != null)
                {
                    var tokenString = context.Request.Headers["Authorization"]
                        .FirstOrDefault()?.Split(" ").Last();
                    if (!string.IsNullOrEmpty(tokenString))
                    {
                        var isBlacklisted = await blacklistService
                            .IsTokenBlacklistedAsync(tokenString);
                        if (isBlacklisted)
                        {
                            logger.LogWarning("Blacklisted token used by {User}", username);
                            context.Fail("This token has been blacklisted.");
                            return;
                        }
                    }
                }

                // Validate session claims
                var userIdRaw = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var sid = context.Principal?.FindFirst("sid")?.Value;
                var svRaw = context.Principal?.FindFirst("sv")?.Value;
                var iph = context.Principal?.FindFirst("iph")?.Value;

                if (!int.TryParse(userIdRaw, out var userId) ||
                    string.IsNullOrWhiteSpace(sid) ||
                    !long.TryParse(svRaw, out var sv) ||
                    string.IsNullOrWhiteSpace(iph))
                {
                    context.Fail("Invalid session claims.");
                    return;
                }

                // Validate session state
                var sessionCacheKey = $"auth:session:user:{userId}";
                var sessionLoadedFromRedis = false;
                UserSessionState? sessionState = null;
                try
                {
                    sessionState = await cacheService
                        .GetAsync<UserSessionState>(sessionCacheKey);
                    sessionLoadedFromRedis = sessionState != null;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read session cache for {UserId}", userId);
                }

                var userEntity = await userRepository.GetByIdAsync(userId);

                if (sessionState == null)
                {
                    if (userEntity == null ||
                        string.IsNullOrWhiteSpace(userEntity.CurrentSessionId) ||
                        string.IsNullOrWhiteSpace(userEntity.LastLoginIpHash))
                    {
                        context.Fail("Session not found.");
                        return;
                    }

                    sessionState = new UserSessionState
                    {
                        SessionId = userEntity.CurrentSessionId,
                        SessionVersion = userEntity.SessionVersion,
                        IpHash = userEntity.LastLoginIpHash,
                        DeviceBindingHash = userEntity.LastLoginDeviceHash,
                        UpdatedAt = DateTime.UtcNow
                    };

                    try
                    {
                        await cacheService.SetAsync(
                            sessionCacheKey, sessionState, TimeSpan.FromDays(7));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to write session cache for {UserId}", userId);
                    }
                }

                if (userEntity == null)
                {
                    context.Fail("Session not found.");
                    return;
                }

                // Validate IP binding
                var forwardedIp = httpContext.Request.Headers["X-Forwarded-For"]
                    .FirstOrDefault();
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
                var clientIp = string.IsNullOrWhiteSpace(forwardedIp) ? remoteIp : forwardedIp;

                var fingerprintSecret = configuration["AuthSecurity:FingerprintSecret"]
                    ?? "fallback-secret";
                var currentIpHash = IpBindingHelper.ComputeIpHash(
                    clientIp ?? "unknown", fingerprintSecret);

                if (sessionState.SessionId != sid ||
                    sessionState.SessionVersion != sv ||
                    sessionState.IpHash != iph ||
                    sessionState.IpHash != currentIpHash)
                {
                    context.Fail("Session invalidated.");
                    return;
                }

                var deviceIdHeader = httpContext.Request.Headers["X-Device-Id"].FirstOrDefault();
                var jwtDbh = context.Principal?.FindFirst("dbh")?.Value;
                try
                {
                    await deviceBindingValidation.ValidateAsync(
                        deviceIdHeader,
                        jwtDbh,
                        sessionLoadedFromRedis,
                        sessionState,
                        userEntity);
                }
                catch (DeviceValidationException ex)
                {
                    httpContext.Items["DeviceValidationReason"] = ex.Reason;
                    context.Fail(ex.Message);
                    return;
                }

                var isDeviceBound = !string.IsNullOrWhiteSpace(jwtDbh) ||
                                    !string.IsNullOrWhiteSpace(sessionState.DeviceBindingHash);
                httpContext.Items[RequestDeviceContextKeys.IsDeviceBound] = isDeviceBound;
                if (isDeviceBound)
                {
                    httpContext.Items[RequestDeviceContextKeys.NormalizedDeviceId] =
                        DeviceBindingHelper.NormalizeDeviceId(deviceIdHeader);
                }
                else
                {
                    httpContext.Items.Remove(RequestDeviceContextKeys.NormalizedDeviceId);
                }

                try
                {
                    await deviceSessionService.UpdateLastSeenAsync(sid);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "UpdateLastSeenAsync failed for session {SessionId}", sid);
                }

                logger.LogInformation("JWT validated for user {User}", username);
            },

            OnChallenge = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Unauthorized request to {Path}",
                    context.HttpContext.Request.Path);

                context.HandleResponse();
                context.Response.ContentType = "application/json";

                if (context.HttpContext.Items.TryGetValue("DeviceValidationReason", out var reasonObj) &&
                    reasonObj is DeviceValidationResult reason)
                {
                    var (statusCode, errorCode, message) = reason switch
                    {
                        DeviceValidationResult.MissingHeader => (400, "DEVICE_HEADER_MISSING", "X-Device-Id header is required"),
                        DeviceValidationResult.DeviceMismatch => (401, "DEVICE_MISMATCH", "Token used from unrecognized device"),
                        DeviceValidationResult.SessionRevoked => (401, "SESSION_REVOKED", "Session has been revoked. Please login again"),
                        DeviceValidationResult.SessionRotated => (401, "SESSION_ROTATED", "Session was replaced. Please re-authenticate"),
                        _ => (401, "DEVICE_INVALID", "Device validation failed")
                    };

                    context.Response.StatusCode = statusCode;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        statusCode = statusCode,
                        Success = false,
                        ErrorCode = errorCode,
                        Message = message
                    });
                    return;
                }

                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    statusCode = 401,
                    success = false,
                    errorCode = "UNAUTHORIZED",
                    message = "You are not authorized to access this resource"
                });
            },

            OnForbidden = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Forbidden request by {User} to {Path}",
                    context.HttpContext.User.Identity?.Name,
                    context.HttpContext.Request.Path);

                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    statusCode = 403,
                    success = false,
                    errorCode = "FORBIDDEN",
                    message = "You do not have permission"
                });
            }
        };
    }
}