using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces
{
    public interface IRoleRepository
    {
        Task<Role?> GetByIdAsync(int id);

        Task<List<Role>> GetAllAsync();

        Task AddAsync(Role role);

        Task UpdateAsync(Role role);

        Task DeleteAsync(Role role);

        Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds);

        Task AssignRoleToUserAsync(int userId, int roleId);

        Task<Role?> GetByNameAsync(string name);

        Task<List<int>> GetUserIdsByRoleIdAsync(int roleId);
    }
}
