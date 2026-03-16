using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces
{
    public interface IPermissionRepository
    {
        Task<List<Permission>> GetAllAsync();

        Task<List<Permission>> GetByRoleIdAsync(Guid roleId);

        Task<List<string>> GetPermissionsByUserIdAsync(int userId);
    }
}
