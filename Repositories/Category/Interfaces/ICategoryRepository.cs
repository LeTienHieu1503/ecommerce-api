using Ecommerce.API.Models;

namespace Ecommerce.API.Repositories.Interfaces;

public interface ICategoryRepository
{
    IQueryable<Category> GetQueryable();

    Task<Category?> GetByIdAsync(int id);

    Task AddAsync(Category category);

    Task<bool> HasProductsAsync(int categoryId);

    Task SaveChangesAsync();
}