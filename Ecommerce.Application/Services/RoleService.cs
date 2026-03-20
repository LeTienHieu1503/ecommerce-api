using Ecommerce.Application.DTOs.User;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;

namespace Ecommerce.Application.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;
    private const string PermissionsKeyPrefix = "permissions:";

    public RoleService(IRoleRepository roleRepository, IUserRepository userRepository, ICacheService cacheService)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _cacheService = cacheService;
    }

    public async Task<int> CreateRoleAsync(string name)
    {
        var role = new Role
        {
            Name = name
        };

        await _roleRepository.AddAsync(role);

        return role.Id;
    }

    public async Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds)
    {
        await _roleRepository.AssignPermissionsAsync(roleId, permissionIds);
        var affectedUserIds = await _roleRepository.GetUserIdsByRoleIdAsync(roleId);

        foreach (var userId in affectedUserIds)
        {
            await _cacheService.RemoveAsync($"{PermissionsKeyPrefix}{userId}");
        }
    }

    public async Task AssignRoleToUserAsync(int userId, int roleId)
    {
        await _roleRepository.AssignRoleToUserAsync(userId, roleId);
        await _cacheService.RemoveAsync($"{PermissionsKeyPrefix}{userId}");
    }

    public async Task<IReadOnlyList<(int Id, string Name, IReadOnlyList<string> Permissions)>> GetRolesAsync()
    {
        var roles = await _roleRepository.GetAllWithPermissionsAsync();

        return roles
            .Select(r =>
                (
                    r.Id,
                    r.Name,
                    (IReadOnlyList<string>)r.RolePermissions
                        .Where(rp => rp.Permission != null)
                        .Select(rp => rp.Permission!.Name)
                        .OrderBy(n => n)
                        .ToList()
                )
            ).ToList();
    }

    public async Task<IReadOnlyList<UserDto>> GetAllUsersWithRolesAsync()
    {
        var users = await _userRepository.GetAllWithRolesAsync();

        return users.Select(u => new UserDto(
            u.Id,
            u.Email,
            u.UserRoles.FirstOrDefault()?.Role.Name ?? "None"
        )).ToList();
    }

    public async Task UpdateRoleAsync(int id, string name)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
            throw new Exception("Role not found");

        role.Name = name;
        await _roleRepository.UpdateAsync(role);
    }

    public async Task DeleteRoleAsync(int id)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
            throw new Exception("Role not found");

        await _roleRepository.DeleteAsync(role);
    }
}
