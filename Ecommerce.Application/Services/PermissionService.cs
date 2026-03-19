using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Interfaces;

namespace Ecommerce.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ICacheService _cacheService;
    private const string PermissionsKeyPrefix = "permissions:";
    private static readonly SemaphoreSlim _permissionsLock = new SemaphoreSlim(1, 1);

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

        await _permissionsLock.WaitAsync();
        try
        {
            cached = await _cacheService.GetAsync<List<string>>(cacheKey);
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
        finally
        {
            _permissionsLock.Release();
        }
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync()
    {
        var permissions = await _permissionRepository.GetAllAsync();

        return permissions
            .Select(p => new PermissionDto(p.Id, p.Name, p.Entity, p.Action))
            .ToList();
    }

    public async Task<int> CreatePermissionAsync(string entity, string action)
    {
        var name = $"{entity}.{action}";
        var permission = new Ecommerce.Domain.Entities.Permission
        {
            Entity = entity,
            Action = action,
            Name = name
        };

        await _permissionRepository.AddAsync(permission);
        return permission.Id;
    }

    public async Task UpdatePermissionAsync(int id, string entity, string action)
    {
        var permission = await _permissionRepository.GetByIdAsync(id);
        if (permission == null)
            throw new Exception("Permission not found");

        permission.Entity = entity;
        permission.Action = action;
        permission.Name = $"{entity}.{action}";

        await _permissionRepository.UpdateAsync(permission);
    }

    public async Task DeletePermissionAsync(int id)
    {
        var permission = await _permissionRepository.GetByIdAsync(id);
        if (permission == null)
            throw new Exception("Permission not found");

        await _permissionRepository.DeleteAsync(permission);
    }
}
