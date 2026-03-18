using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<User?> GetByIdAsync(int id);

    Task<bool> ExistsByEmailAsync(string email);

    Task AddAsync(User user);

    Task SaveChangesAsync();

    Task<IReadOnlyList<User>> GetAllWithRolesAsync();
}
