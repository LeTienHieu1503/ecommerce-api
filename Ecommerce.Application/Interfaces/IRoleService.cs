namespace Ecommerce.Application.Interfaces;

public interface IRoleService
{
    Task<Guid> CreateRoleAsync(string name);

    Task AssignPermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds);

    Task AssignRoleToUserAsync(int userId, Guid roleId);

    Task<IReadOnlyList<(Guid Id, string Name)>> GetRolesAsync();
}
