namespace Ecommerce.Application.Interfaces;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(int userId);

    Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync();

    Task<int> CreatePermissionAsync(string entity, string action);

    Task UpdatePermissionAsync(int id, string entity, string action);

    Task DeletePermissionAsync(int id);
}

public record PermissionDto(int Id, string Name, string Entity, string Action);
