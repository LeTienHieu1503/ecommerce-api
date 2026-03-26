using Ecommerce.Application.DTOs.User;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required", nameof(name));

        var trimmed = name.Trim();
        var existing = await _roleRepository.GetByNameAsync(trimmed);
        if (existing != null)
            throw new ConflictException("Role name already exists");

        var role = new Role
        {
            Name = trimmed
        };

        await _roleRepository.AddAsync(role);

        return role.Id;
    }

    public async Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds)
    {
        var ids = permissionIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
            return;

        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role == null)
            throw new NotFoundException("Role not found");

        var existingIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var alreadyAssigned = ids.Where(existingIds.Contains).ToList();
        if (alreadyAssigned.Count > 0)
            throw new ConflictException($"Permission(s) already assigned to role: {string.Join(", ", alreadyAssigned)}");

        await _roleRepository.AssignPermissionsAsync(roleId, ids);
        await InvalidatePermissionCacheForRoleUsersAsync(roleId);
    }

    public async Task RemovePermissionsAsync(int roleId, IEnumerable<int> permissionIds)
    {
        var ids = permissionIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
            return;

        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role == null)
            throw new NotFoundException("Role not found");

        var existingIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var notAssigned = ids.Where(id => !existingIds.Contains(id)).ToList();
        if (notAssigned.Count > 0)
            throw new BusinessException($"Permission(s) not assigned to role: {string.Join(", ", notAssigned)}");

        await _roleRepository.RemovePermissionsAsync(roleId, ids);
        await InvalidatePermissionCacheForRoleUsersAsync(roleId);
    }

    public async Task AssignRoleToUserAsync(int userId, int roleId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role == null)
            throw new NotFoundException("Role not found");

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
            throw new NotFoundException("Role not found");

        role.Name = name;
        await _roleRepository.UpdateAsync(role);
    }

    public async Task DeleteRoleAsync(int id)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
            throw new NotFoundException("Role not found");

        await _roleRepository.DeleteAsync(role);
    }

    private async Task InvalidatePermissionCacheForRoleUsersAsync(int roleId)
    {
        var affectedUserIds = await _roleRepository.GetUserIdsByRoleIdAsync(roleId);
        foreach (var userId in affectedUserIds)
        {
            await _cacheService.RemoveAsync($"{PermissionsKeyPrefix}{userId}");
        }
    }
}
