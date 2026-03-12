using Ecommerce.API.Data;
using Ecommerce.API.Models;
using Ecommerce.API.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.API.Repositories.Implementations;

public class ProductRepository : IProductRepository
{
    private readonly ApplicationDbContext _context;

    public ProductRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public IQueryable<Product> GetQueryable()
    {
        return _context.Products
            .Include(p => p.Category)
            .Where(p => !p.IsDeleted)
            .AsNoTracking();
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        return await _context.Products
            .Include(p => p.Category)
            //.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
    }

    public async Task AddAsync(Product product)
    {
        await _context.Products.AddAsync(product);
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        return await _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id == categoryId && !c.IsDeleted);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}