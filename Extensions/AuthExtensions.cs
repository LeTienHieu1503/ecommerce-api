using Ecommerce.API.Authorization.Handlers;
using Ecommerce.API.Authorization.Policies;
using Ecommerce.API.Authorization.Requirements;
using Ecommerce.Application.Common.Security;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.Interfaces;
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
                    RoleClaimType = ClaimTypes.Role
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
                "order.create", "order.read", "order.update", "order.delete"
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
                UserSessionState? sessionState = null;
                try
                {
                    sessionState = await cacheService
                        .GetAsync<UserSessionState>(sessionCacheKey);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read session cache for {UserId}", userId);
                }

                if (sessionState == null)
                {
                    var user = await userRepository.GetByIdAsync(userId);
                    if (user == null ||
                        string.IsNullOrWhiteSpace(user.CurrentSessionId) ||
                        string.IsNullOrWhiteSpace(user.LastLoginIpHash))
                    {
                        context.Fail("Session not found.");
                        return;
                    }

                    sessionState = new UserSessionState
                    {
                        SessionId = user.CurrentSessionId,
                        SessionVersion = user.SessionVersion,
                        IpHash = user.LastLoginIpHash,
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

                logger.LogInformation("JWT validated for user {User}", username);
            },

            OnChallenge = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Unauthorized request to {Path}",
                    context.HttpContext.Request.Path);

                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";

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