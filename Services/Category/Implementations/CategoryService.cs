using Ecommerce.API.Common.Pagination;
using Ecommerce.API.Common.Sorting;
using Ecommerce.API.Data;
using Ecommerce.API.DTOs.Category;
using Ecommerce.API.Exceptions;
using Ecommerce.API.Models;
using Ecommerce.API.Repositories.Interfaces;
using Ecommerce.API.Services.Category.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.API.Services.Category.Implementations;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;

    public CategoryService(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<PagedResult<CategoryResponseDto>> GetAllAsync(CategoryQuery query)
    {
        var categories = _categoryRepository.GetQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            categories = categories.Where(c => c.Name.StartsWith(query.Search));
        }

        categories = query.SortBy?.ToLower() switch
        {
            "name" => query.SortOrder == "desc"
                ? categories.OrderByDescending(c => c.Name)
                : categories.OrderBy(c => c.Name),

            _ => categories.OrderBy(c => c.Id)
        };

        var dtoQuery = categories.Select(c => new CategoryResponseDto
        {
            Id = c.Id,
            Name = c.Name,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        });

        return await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);
    }

    public async Task<CategoryResponseDto> GetByIdAsync(int id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);

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
        var category = new Models.Category
        {
            Name = dto.Name
        };

        await _categoryRepository.AddAsync(category);
        await _categoryRepository.SaveChangesAsync();

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
        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
            throw new NotFoundException("Category not found");

        category.Name = dto.Name;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.SaveChangesAsync();

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
        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
            throw new NotFoundException("Category not found");

        var hasProducts = await _categoryRepository.HasProductsAsync(id);

        if (hasProducts)
            throw new BusinessException("Cannot delete category because it has products.");

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.SaveChangesAsync();
    }
}