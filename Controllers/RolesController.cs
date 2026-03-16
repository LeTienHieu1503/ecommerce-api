using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Authorization.Policies.AuthorizationPolicies.AdminOnly)]
public class RolesController : BaseApiController
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public record CreateRoleRequest(string Name);

    public record AssignPermissionsRequest(IEnumerable<Guid> PermissionIds);

    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
    {
        var id = await _roleService.CreateRoleAsync(request.Name);
        return CreatedSuccess(new { id, name = request.Name });
    }

    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _roleService.GetRolesAsync();
        var response = roles.Select(r => new { id = r.Id, name = r.Name });
        return Success(response);
    }

    [HttpPost("{roleId:guid}/permissions")]
    public async Task<IActionResult> AssignPermissions(
        Guid roleId,
        [FromBody] AssignPermissionsRequest request)
    {
        await _roleService.AssignPermissionsAsync(roleId, request.PermissionIds);
        return NoContent();
    }
}
