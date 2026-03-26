using System.Linq;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ecommerce.API.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerConfig(
        this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
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

            options.OperationFilter<SwaggerDeviceIdHeaderOperationFilter>();

            options.TagActionsBy(api =>
{
                var controller = api.ActionDescriptor.RouteValues["controller"];
                return controller switch
                {
                    "Auth" => new[] { "1. Auth" },
                    "Category" => new[] { "2. Categories" },
                    "Product" => new[] { "3. Products" },
                    "Order" => new[] { "4. Orders" },
                    "Users" => new[] { "5. Users" },
                    "Roles" => new[] { "6. Roles" },
                    "Permissions" => new[] { "7. Permissions" },
                    _ => new[] { $"{controller}" }
                };
            });
        });

        return services;
    }

    public static WebApplication UseSwaggerConfig(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        return app;
    }
}

internal sealed class SwaggerDeviceIdHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters?.Any(p =>
                p.In == ParameterLocation.Header &&
                string.Equals(p.Name, "X-Device-Id", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Device-Id",
            In = ParameterLocation.Header,
            Description =
                "Optional. If login included deviceId, send the same value here on every request (and refresh).",
            Required = false,
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}