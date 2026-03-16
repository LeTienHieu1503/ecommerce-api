using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Authorization.Policies.AuthorizationPolicies.AdminOnly)]
public class PermissionsController : BaseApiController
{
    private readonly IPermissionService _permissionService;

    public PermissionsController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPermissions()
    {
        var permissions = await _permissionService.GetAllPermissionsAsync();

        var response = permissions.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            entity = p.Entity,
            action = p.Action
        });

        return Success(response);
    }
}
