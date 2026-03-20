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

    public record CreatePermissionRequest(string Entity, string Action);

    [HttpPost]
    public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest request)
    {
        var id = await _permissionService.CreatePermissionAsync(request.Entity, request.Action);
        var payload = new
        {
            id,
            name = $"{request.Entity}.{request.Action}",
            entity = request.Entity,
            action = request.Action
        };
        return CreatedSuccess(payload, "Create permission successfully");
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdatePermission(int id, [FromBody] CreatePermissionRequest request)
    {
        await _permissionService.UpdatePermissionAsync(id, request.Entity, request.Action);
        var payload = new
        {
            id,
            name = $"{request.Entity}.{request.Action}",
            entity = request.Entity,
            action = request.Action
        };
        return Success(payload, "Update permission successfully");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePermission(int id)
    {
        await _permissionService.DeletePermissionAsync(id);
        return Success(new { id, deleted = true }, "Delete permission successfully");
    }
}