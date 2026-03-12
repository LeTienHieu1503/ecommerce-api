using Ecommerce.API.Data;
using Ecommerce.API.Models;
using Ecommerce.API.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.API.Repositories.Implementations;

public class CategoryRepository : ICategoryRepository
{
    private readonly ApplicationDbContext _context;

    public CategoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public IQueryable<Category> GetQueryable()
    {
        return _context.Categories
            .Where(c => !c.IsDeleted)
            .AsNoTracking()
            .AsQueryable();
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        return await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
    }

    public async Task AddAsync(Category category)
    {
        await _context.Categories.AddAsync(category);
    }

    public async Task<bool> HasProductsAsync(int categoryId)
    {
        return await _context.Products
            .AnyAsync(p => p.CategoryId == categoryId && !p.IsDeleted);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}