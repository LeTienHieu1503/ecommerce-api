using Ecommerce.Application.Common.Http;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<PermissionService> _logger;
    private readonly IRequestDeviceContext _requestDeviceContext;
    private const string PermissionsKeyPrefix = "permissions:";
    private static readonly SemaphoreSlim _permissionsLock = new SemaphoreSlim(1, 1);

    public PermissionService(
        IPermissionRepository permissionRepository,
        ICacheService cacheService,
        ILogger<PermissionService> logger,
        IRequestDeviceContext requestDeviceContext)
    {
        _permissionRepository = permissionRepository;
        _cacheService = cacheService;
        _logger = logger;
        _requestDeviceContext = requestDeviceContext;
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(int userId)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(PermissionService), nameof(GetUserPermissionsAsync));
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
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(PermissionService), nameof(GetAllPermissionsAsync));
        var permissions = await _permissionRepository.GetAllAsync();

        return permissions
            .Select(p => new PermissionDto(p.Id, p.Name, p.Entity, p.Action))
            .ToList();
    }

    public async Task<int> CreatePermissionAsync(string entity, string action)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(PermissionService), nameof(CreatePermissionAsync));
        var all = await _permissionRepository.GetAllAsync();
        if (all.Any(p =>
                string.Equals(p.Entity, entity, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Action, action, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("Permission already exists");

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
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(PermissionService), nameof(UpdatePermissionAsync));
        var permission = await _permissionRepository.GetByIdAsync(id);
        if (permission == null)
            throw new NotFoundException("Permission not found");

        var all = await _permissionRepository.GetAllAsync();
        if (all.Any(p =>
                p.Id != id &&
                string.Equals(p.Entity, entity, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Action, action, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("Permission already exists");

        permission.Entity = entity;
        permission.Action = action;
        permission.Name = $"{entity}.{action}";

        await _permissionRepository.UpdateAsync(permission);
    }

    public async Task DeletePermissionAsync(int id)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(PermissionService), nameof(DeletePermissionAsync));
        var permission = await _permissionRepository.GetByIdAsync(id);
        if (permission == null)
            throw new NotFoundException("Permission not found");

        await _permissionRepository.DeleteAsync(permission);
    }
}
