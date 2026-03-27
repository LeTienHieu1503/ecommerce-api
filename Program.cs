using Ecommerce.API.Extensions;
using Ecommerce.API.Middleware;
using Ecommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Context;

EnvLoader.LoadLocalDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{RequestId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Services
builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplicationServices()
    .AddAuthConfig(builder.Configuration)
    .AddSwaggerConfig()
    .AddControllers();

// Validation response
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

        return new BadRequestObjectResult(new
        {
            StatusCode = 400,
            Success = false,
            ErrorCode = "VALIDATION_ERROR",
            Message = "Validation failed",
            Errors = errors
        });
    };
});

// Middleware Pipeline
var app = builder.Build();

app.UseSwaggerConfig();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();

// Request logging
app.Use(async (context, next) =>
{
    var requestId = context.TraceIdentifier;
    using (LogContext.PushProperty("RequestId", requestId))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await next();
        logger.LogInformation("User {User} -> {Method} {Path} -> {StatusCode} ({Elapsed} ms)",
            context.User?.Identity?.Name ?? "anonymous",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Database Migration + Seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var retries = 5;
    while (retries > 0)
    {
        try { await db.Database.MigrateAsync(); break; }
        catch { retries--; await Task.Delay(2000); }
    }

    await AdminSeeder.SeedAdminAsync(db);
    await PermissionSeeder.SeedPermissionsAsync(db);
}

app.Run();