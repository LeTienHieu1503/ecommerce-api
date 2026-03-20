using System.Linq.Expressions;
using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces;

public interface ICategoryRepository
{
    IQueryable<Category> GetQueryable();
    Task<Category?> GetByIdAsync(int id);
    Task<bool> ExistsAsync(Expression<Func<Category, bool>> predicate);
    Task AddAsync(Category category);
    Task<bool> HasProductsAsync(int categoryId);
    Task SaveChangesAsync();
}
