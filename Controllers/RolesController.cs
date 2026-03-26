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

    public record AssignPermissionsRequest(IEnumerable<int> PermissionIds);

    public record RemovePermissionsRequest(IEnumerable<int> PermissionIds);

    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
    {
        var id = await _roleService.CreateRoleAsync(request.Name);
        return CreatedSuccess(new { id, name = request.Name }, "Create role successfully");
    }

    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _roleService.GetRolesAsync();
        var response = roles.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            permissions = r.Permissions
        });
        return Success(response);
    }

    [HttpPost("{roleId:int}/permissions")]
    public async Task<IActionResult> AssignPermissions(
        int roleId,
        [FromBody] AssignPermissionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _roleService.AssignPermissionsAsync(roleId, request.PermissionIds);
        var permissionIds = request.PermissionIds?.ToList() ?? new List<int>();
        return Success(new { roleId, permissionIds }, "Assign permissions successfully");
    }

    [HttpDelete("{roleId:int}/permissions")]
    public async Task<IActionResult> RemovePermissions(
        int roleId,
        [FromBody] RemovePermissionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _roleService.RemovePermissionsAsync(roleId, request.PermissionIds);
        var permissionIds = request.PermissionIds?.ToList() ?? new List<int>();
        return Success(new { roleId, permissionIds }, "Remove permissions successfully");
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] CreateRoleRequest request)
    {
        await _roleService.UpdateRoleAsync(id, request.Name);
        return Success(new { id, name = request.Name }, "Update role successfully");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRole(int id)
    {
        await _roleService.DeleteRoleAsync(id);
        return Success(new { id, deleted = true }, "Delete role successfully");
    }
}