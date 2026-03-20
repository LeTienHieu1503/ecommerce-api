using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Authorization.Policies.AuthorizationPolicies.AdminOnly)]
public class UsersController : BaseApiController
{
    private readonly IRoleService _roleService;

    public UsersController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public record AssignRoleRequest(int RoleId);

    [HttpGet]
    public async Task<IActionResult> GetAllUser()
    {
        var users = await _roleService.GetAllUsersWithRolesAsync();
        return Success(users, "Get all users successfully");
    }

    [HttpPost("{userId:int}/roles")]
    public async Task<IActionResult> AssignRoleToUser(int userId, [FromBody] AssignRoleRequest request)
    {
        await _roleService.AssignRoleToUserAsync(userId, request.RoleId);
        return Success(new { userId, roleId = request.RoleId }, "Assign role successfully");
    }
}