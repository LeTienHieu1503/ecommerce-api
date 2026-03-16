namespace Ecommerce.Application.Interfaces;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(int userId);

    Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync();
}

public record PermissionDto(Guid Id, string Name, string Entity, string Action);
