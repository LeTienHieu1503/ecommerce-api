using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
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
                Description = "JWT access token (paste token only, without 'Bearer ' prefix)",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityDefinition("DeviceId", new OpenApiSecurityScheme
            {
                Description =
                    "Same value as deviceId at login. Leave empty if login had no deviceId. Applied once via Authorize for all secured calls.",
                Name = "X-Device-Id",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey
            });

            options.OperationFilter<SwaggerSecurityRequirementsOperationFilter>();

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
                    "Webhook" => new[] { "8. Webhooks" },
                    "Device" => new[] { "9. Devices" },
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

/// <summary>
/// Assigns OpenAPI security per operation: Bearer+DeviceId for [Authorize], DeviceId-only for refresh,
/// none for public endpoints. DeviceId is set once in Swagger Authorize alongside JWT.
/// </summary>
internal sealed class SwaggerSecurityRequirementsOperationFilter : IOperationFilter
{
    private static readonly OpenApiSecurityScheme BearerSchemeRef = new()
    {
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    private static readonly OpenApiSecurityScheme DeviceIdSchemeRef = new()
    {
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "DeviceId" }
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.Any(m => m is IAllowAnonymous))
        {
            if (IsRefreshEndpoint(context))
            {
                operation.Security = new List<OpenApiSecurityRequirement>
                {
                    new OpenApiSecurityRequirement
                    {
                        [DeviceIdSchemeRef] = Array.Empty<string>()
                    }
                };
            }

            return;
        }

        if (metadata.Any(m => m is AuthorizeAttribute) ||
            ControllerHasAuthorize(context))
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    [BearerSchemeRef] = Array.Empty<string>(),
                    [DeviceIdSchemeRef] = Array.Empty<string>()
                }
            };
        }
    }

    private static bool ControllerHasAuthorize(OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor cad)
            return false;

        return cad.ControllerTypeInfo.GetCustomAttributes(inherit: true).OfType<AuthorizeAttribute>().Any();
    }

    private static bool IsRefreshEndpoint(OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        return path.Contains("auth/refresh", StringComparison.OrdinalIgnoreCase);
    }
}
