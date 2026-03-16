using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces
{
    public interface IRoleRepository
    {
        Task<Role?> GetByIdAsync(Guid id);

        Task<List<Role>> GetAllAsync();

        Task AddAsync(Role role);

        Task UpdateAsync(Role role);

        Task DeleteAsync(Role role);

        Task AssignPermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds);

        Task AssignRoleToUserAsync(int userId, Guid roleId);
    }
}
