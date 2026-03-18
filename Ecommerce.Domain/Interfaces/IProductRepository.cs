using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces;

public interface IProductRepository
{
    IQueryable<Product> GetQueryable();
    Task<Product?> GetByIdAsync(int id);
    Task AddAsync(Product product);
    Task<bool> CategoryExistsAsync(int categoryId);
    void UpdateConcurrencyToken(Product product, byte[] rowVersion);
    Task SaveChangesAsync();
}
