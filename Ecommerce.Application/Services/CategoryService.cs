using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Application.Common.Sorting;
using Ecommerce.Application.DTOs.Category;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(
    ICategoryRepository categoryRepository,
    ILogger<CategoryService> logger)
    {
        _categoryRepository = categoryRepository;
        _logger = logger;
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
        _logger.LogInformation("Getting category {CategoryId}", id);

        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            _logger.LogWarning("Category not found {CategoryId}", id);
            throw new NotFoundException("Category not found");
        }

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
        _logger.LogInformation(
            "Creating category {CategoryName}",
            dto.Name);

        var category = new Category
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
        _logger.LogInformation("Updating category {CategoryId}", id);

        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            _logger.LogWarning("Category not found {CategoryId}", id);
            throw new NotFoundException("Category not found");
        }

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
        _logger.LogInformation("Deleting category {CategoryId}", id);

        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            _logger.LogWarning("Category not found {CategoryId}", id);
            throw new NotFoundException("Category not found");
        }

        var hasProducts = await _categoryRepository.HasProductsAsync(id);

        if (hasProducts)
        {
            _logger.LogWarning(
                "Cannot delete category {CategoryId} because it has products",
                id);

            throw new BusinessException(
                "Cannot delete category because it has products.");
        }

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.SaveChangesAsync();
    }
}