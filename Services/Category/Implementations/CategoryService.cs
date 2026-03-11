using Ecommerce.API.Common.Pagination;
using Ecommerce.API.Common.Sorting;
using Ecommerce.API.Data;
using Ecommerce.API.DTOs.Category;
using Ecommerce.API.Exceptions;
using Ecommerce.API.Models;
using Ecommerce.API.Services.Category.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.API.Services.Category.Implementations;

public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _context;

    public CategoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CategoryResponseDto>> GetAllAsync(CategoryQuery query)
    {
        var categories = _context.Categories
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            categories = categories.Where(c => c.Name.Contains(query.Search));
        }

        //categories = categories.ApplySorting(query.SortBy, query.SortOrder);

        var dtoQuery = categories
            .Where(c => !c.IsDeleted)
            .Select(c => new CategoryResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .AsNoTracking();

        return await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);
    }

    public async Task<CategoryResponseDto> GetByIdAsync(int id)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

        if (category == null)
            throw new NotFoundException("Category not found");

        return new CategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }

    public async Task<CategoryResponseDto> CreateAsync(CreateCategoryDto dto)
    {

        var category = new Ecommerce.API.Models.Category
        {
            Name = dto.Name,
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return new CategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }

    public async Task<CategoryResponseDto> UpdateAsync(int id, UpdateCategoryDto dto)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new NotFoundException("Category not found");

        category.Name = dto.Name;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new CategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }

    public async Task DeleteAsync(int id)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new NotFoundException("Category not found");

        var hasProducts = await _context.Products
            .AnyAsync(p => p.CategoryId == id);

        if (hasProducts)
            throw new BusinessException("Cannot delete category because it has products.");

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }
}