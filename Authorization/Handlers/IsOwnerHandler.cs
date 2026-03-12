using Ecommerce.API.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Ecommerce.API.Authorization.Handlers;

public class IsOwnerHandler
    : AuthorizationHandler<IsOwnerRequirement, int>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IsOwnerRequirement requirement,
        int resourceUserId)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userId != null && userId == resourceUserId.ToString())
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}