using System.Security.Claims;
using Ecommerce.API.Authorization.Requirements;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.API.Authorization.Handlers;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;

    public PermissionHandler(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)
                         ?? context.User.FindFirst("sub");

        if (userIdClaim == null)
        {
            return;
        }

        if (!int.TryParse(userIdClaim.Value, out var userId))
        {
            return;
        }

        var permissions = await _permissionService.GetUserPermissionsAsync(userId);

        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
