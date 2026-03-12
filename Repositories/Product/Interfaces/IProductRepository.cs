using Ecommerce.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.API.Repositories.Interfaces;

public interface IProductRepository
{
    IQueryable<Product> GetQueryable();

    Task<Product?> GetByIdAsync(int id);

    Task AddAsync(Product product);

    Task<bool> CategoryExistsAsync(int categoryId);

    Task SaveChangesAsync();
}