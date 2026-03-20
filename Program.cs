using Ecommerce.API.Authorization.Policies;
using Ecommerce.Application.Common.Security;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.API.Authorization.Handlers;
using Ecommerce.API.Authorization.Requirements;
using Ecommerce.API.Middleware;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Infrastructure.Caching;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authorization;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

//Log
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()

    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)

    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{RequestId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{RequestId}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

builder.Host.UseSerilog();

// Register DbContext and configure SQL Server connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Read JWT settings from appsettings.json
var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"] ?? throw new Exception("JWT Key is missing");

// Configure JWT Bearer Authentication
builder.Services
.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    var jwt = builder.Configuration.GetSection("Jwt");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey)
        ),

        // DefaultInboundClaimTypeMap maps "role" → ClaimTypes.Role on decode;
        // RoleClaimType must match the final decoded claim type
        RoleClaimType = ClaimTypes.Role
    };

    // Customize responses for authentication/authorization failures
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            logger.LogWarning(
                "JWT authentication failed: {Message}",
                context.Exception.Message);

            return Task.CompletedTask;
        },

        OnTokenValidated = async context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            var username = context.Principal?.Identity?.Name;
            var httpContext = context.HttpContext;

            var blacklistService = httpContext.RequestServices
                .GetRequiredService<ITokenBlacklistService>();
            var cacheService = httpContext.RequestServices
                .GetRequiredService<ICacheService>();
            var userRepository = httpContext.RequestServices
                .GetRequiredService<IUserRepository>();
            var configuration = httpContext.RequestServices
                .GetRequiredService<IConfiguration>();

            if (context.SecurityToken != null)
            {
                var tokenString = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
                if (!string.IsNullOrEmpty(tokenString))
                {
                    var isBlacklisted = await blacklistService.IsTokenBlacklistedAsync(tokenString);
                    if (isBlacklisted)
                    {
                        logger.LogWarning("Blacklisted token used by user {User}", username);
                        context.Fail("This token has been blacklisted.");
                        return;
                    }
                }
            }

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

            var sessionCacheKey = $"auth:session:user:{userId}";
            UserSessionState? sessionState = null;
            try
            {
                sessionState = await cacheService.GetAsync<UserSessionState>(sessionCacheKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read session cache for user {UserId}", userId);
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
                        sessionCacheKey,
                        sessionState,
                        TimeSpan.FromDays(7));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to write session cache for user {UserId}", userId);
                }
            }

            var forwardedIp = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var clientIp = string.IsNullOrWhiteSpace(forwardedIp) ? remoteIp : forwardedIp;

            var fingerprintSecret = configuration["AuthSecurity:FingerprintSecret"] ?? "fallback-secret";
            var currentIpHash = IpBindingHelper.ComputeIpHash(clientIp ?? "unknown", fingerprintSecret);

            if (sessionState.SessionId != sid ||
                sessionState.SessionVersion != sv ||
                sessionState.IpHash != iph ||
                sessionState.IpHash != currentIpHash)
            {
                context.Fail("Session invalidated.");
                return;
            }

            logger.LogInformation(
                "JWT validated for user {User}",
                username);
        },

        OnChallenge = async context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            logger.LogWarning(
                "Unauthorized request to {Path}",
                context.HttpContext.Request.Path);

            context.HandleResponse();

            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";

            var response = new
            {
                statusCode = 401,
                success = false,
                errorCode = "UNAUTHORIZED",
                message = "You are not authorized to access this resource"
            };

            await context.Response.WriteAsJsonAsync(response);
        },

        OnForbidden = async context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            logger.LogWarning(
                "Forbidden request by user {User} to {Path}",
                context.HttpContext.User.Identity?.Name,
                context.HttpContext.Request.Path);

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var response = new
            {
                statusCode = 403,
                success = false,
                errorCode = "FORBIDDEN",
                message = "You do not have permission"
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    };
});

// Enable Authorization (used with [Authorize] attribute)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.AdminOnly,
        policy => policy.RequireRole("Admin")
    );

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

// Register application services for Dependency Injection
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();
builder.Services.AddScoped<IOrderService, OrderService>();

// Cache: Redis when configured, otherwise in-memory (for local dev without Redis)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
var redisInstanceName = builder.Configuration["Redis:InstanceName"] ?? "EcommerceAPI:";

if (redisEnabled && !string.IsNullOrWhiteSpace(redisConnection))
{
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = false;

    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(options));

    builder.Services.AddStackExchangeRedisCache(opt =>
    {
        opt.Configuration = redisConnection;
        opt.InstanceName = redisInstanceName;
    });

    builder.Services.AddScoped<ICacheService, RedisCacheService>();
}
else
{
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddScoped<ICacheService, MemoryCacheService>();
}

// Register repositories for Dependency Injection
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddControllers();

// Customize the response returned when model validation fails
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .Select(x => new
            {
                Field = x.Key,
                Messages = x.Value?.Errors.Select(e => e.ErrorMessage)
            });

        var response = new
        {
            StatusCode = 400,
            Success = false,
            ErrorCode = "VALIDATION_ERROR",
            Message = "Validation failed",
            Errors = errors
        };

        return new BadRequestObjectResult(response);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter JWT token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseHttpsRedirection();

//Logging
app.Use(async (context, next) =>
{
    var requestId = context.TraceIdentifier;
    using (LogContext.PushProperty("RequestId", requestId))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var method = context.Request.Method;
        var path = context.Request.Path;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await next();
        var statusCode = context.Response.StatusCode;
        var user = context.User?.Identity?.Name ?? "anonymous";
        logger.LogInformation(
            "User {User} -> {Method} {Path} -> {StatusCode} ({Elapsed} ms)",
            user,
            method,
            path,
            statusCode,
            stopwatch.ElapsedMilliseconds
        );
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

//SeedAdmin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    var retries = 5;
    while (retries > 0)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch
        {
            retries--;
            await Task.Delay(2000);
        }
    }

    await AdminSeeder.SeedAdminAsync(db);
    await PermissionSeeder.SeedPermissionsAsync(db);
}

app.Run();
