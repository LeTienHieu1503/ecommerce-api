using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<bool> ExistsByEmailAsync(string email);

    Task AddAsync(User user);

    Task SaveChangesAsync();
}
