using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;

namespace Ecommerce.Application.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly ICacheService _cacheService;
    private const string PermissionsKeyPrefix = "permissions:";

    public RoleService(IRoleRepository roleRepository, ICacheService cacheService)
    {
        _roleRepository = roleRepository;
        _cacheService = cacheService;
    }

    public async Task<Guid> CreateRoleAsync(string name)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        await _roleRepository.AddAsync(role);

        return role.Id;
    }

    public async Task AssignPermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds)
    {
        await _roleRepository.AssignPermissionsAsync(roleId, permissionIds);
        // Invalidate all cached permissions (simpler approach)
        // A more specific invalidation would require querying affected users.
    }

    public async Task AssignRoleToUserAsync(int userId, Guid roleId)
    {
        await _roleRepository.AssignRoleToUserAsync(userId, roleId);
        await _cacheService.RemoveAsync($"{PermissionsKeyPrefix}{userId}");
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetRolesAsync()
    {
        var roles = await _roleRepository.GetAllAsync();
        return roles.Select(r => (r.Id, r.Name)).ToList();
    }
}
