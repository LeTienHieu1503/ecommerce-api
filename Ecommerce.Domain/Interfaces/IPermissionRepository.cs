using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces
{
    public interface IPermissionRepository
    {
        Task<List<Permission>> GetAllAsync();

        Task<List<Permission>> GetByRoleIdAsync(int roleId);

        Task<List<string>> GetPermissionsByUserIdAsync(int userId);

        Task<Permission?> GetByIdAsync(int id);

        Task AddAsync(Permission permission);

        Task UpdateAsync(Permission permission);

        Task DeleteAsync(Permission permission);
    }
}
