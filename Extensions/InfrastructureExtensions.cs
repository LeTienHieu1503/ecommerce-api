using Ecommerce.Domain.Interfaces;
using Ecommerce.Infrastructure.Caching;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Repositories;
using Ecommerce.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Ecommerce.API.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection")));

        // Cache
        services.AddCacheServices(configuration);

        // Repositories
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();

        return services;
    }

    // Cache Services
    private static IServiceCollection AddCacheServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        var redisEnabled = configuration.GetValue<bool>("Redis:Enabled");
        var redisInstanceName = configuration["Redis:InstanceName"] ?? "EcommerceAPI:";

        if (redisEnabled && !string.IsNullOrWhiteSpace(redisConnection))
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(options));

            services.AddStackExchangeRedisCache(opt =>
            {
                opt.Configuration = redisConnection;
                opt.InstanceName = redisInstanceName;
            });

            services.AddScoped<ICacheService, RedisCacheService>();
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddScoped<ICacheService, MemoryCacheService>();
        }

        return services;
    }
}