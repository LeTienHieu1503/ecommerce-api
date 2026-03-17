using Ecommerce.Application.DTOs.User;

namespace Ecommerce.Application.Interfaces;

public interface IRoleService
{
    Task<int> CreateRoleAsync(string name);

    Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds);

    Task AssignRoleToUserAsync(int userId, int roleId);

    Task<IReadOnlyList<(int Id, string Name)>> GetRolesAsync();

    Task<IReadOnlyList<UserDto>> GetAllUsersWithRolesAsync();

    Task UpdateRoleAsync(int id, string name);

    Task DeleteRoleAsync(int id);
}
