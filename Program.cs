using Ecommerce.API.Authorization.Policies;
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
using System.Text;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

//LogFile
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()

    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)

    .WriteTo.Console()
    .WriteTo.File(
        "logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30
    )
    .CreateLogger();

builder.Host.UseSerilog();

//Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration.GetConnectionString("Redis");
    return ConnectionMultiplexer.Connect(configuration);
});

// Register DbContext and configure SQL Server connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Read JWT settings from appsettings.json
var jwt = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwt["Key"]!);

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
            Encoding.UTF8.GetBytes(jwt["Key"]!)
        )
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

        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            var username = context.Principal?.Identity?.Name;

            logger.LogInformation(
                "JWT validated for user {User}",
                username);

            return Task.CompletedTask;
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
        "category.create", "category.read", "category.update", "category.delete"
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

// Cache: Redis when configured, otherwise in-memory (for local dev without Redis)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
var redisInstanceName = builder.Configuration["Redis:InstanceName"] ?? "EcommerceAPI:";

if (redisEnabled && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = redisInstanceName;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// Register repositories for Dependency Injection
builder.Services.AddScoped<ICacheService, RedisCacheService>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddControllers();

// Customize the response returned when model validation fails
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value.Errors.Count > 0)
            .Select(x => new
            {
                Field = x.Key,
                Messages = x.Value.Errors.Select(e => e.ErrorMessage)
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
    var logger = context.RequestServices
        .GetRequiredService<ILogger<Program>>();

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
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

//SeedAdmin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    await db.Database.MigrateAsync();

    await AdminSeeder.SeedAdminAsync(db);
    await PermissionSeeder.SeedPermissionsAsync(db);
}

app.Run();


//dotnet ef migrations add RemoveUserRoleString --project .\Ecommerce.Infrastructure\Ecommerce.Infrastructure.csproj --startup-project .\Ecommerce.API.csproj --context Ecommerce.Infrastructure.Data.ApplicationDbContext
//dotnet ef database update --project .\Ecommerce.Infrastructure\Ecommerce.Infrastructure.csproj --startup-project .\Ecommerce.API.csproj --context Ecommerce.Infrastructure.Data.ApplicationDbContext
//dotnet ef database update 20260311061910_AddUserTable --project .\Ecommerce.Infrastructure\Ecommerce.Infrastructure.csproj --startup-project .\Ecommerce.API.csproj --context Ecommerce.Infrastructure.Data.ApplicationDbContext
//dotnet ef migrations remove --project .\Ecommerce.Infrastructure\Ecommerce.Infrastructure.csproj --startup-project .\Ecommerce.API.csproj --context Ecommerce.Infrastructure.Data.ApplicationDbContext