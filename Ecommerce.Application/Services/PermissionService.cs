using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Interfaces;

namespace Ecommerce.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ICacheService _cacheService;
    private const string PermissionsKeyPrefix = "permissions:";

    public PermissionService(
        IPermissionRepository permissionRepository,
        ICacheService cacheService)
    {
        _permissionRepository = permissionRepository;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(int userId)
    {
        var cacheKey = $"{PermissionsKeyPrefix}{userId}";

        var cached = await _cacheService.GetAsync<List<string>>(cacheKey);
        if (cached is { Count: > 0 })
        {
            return cached;
        }

        var permissions = await _permissionRepository.GetPermissionsByUserIdAsync(userId);

        if (permissions.Count > 0)
        {
            await _cacheService.SetAsync(cacheKey, permissions, TimeSpan.FromMinutes(10));
        }

        return permissions;
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync()
    {
        var permissions = await _permissionRepository.GetAllAsync();

        return permissions
            .Select(p => new PermissionDto(p.Id, p.Name, p.Entity, p.Action))
            .ToList();
    }
}
