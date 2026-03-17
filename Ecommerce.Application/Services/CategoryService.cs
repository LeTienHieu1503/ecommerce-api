using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Application.Common.Sorting;
using Ecommerce.Application.DTOs.Category;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Ecommerce.Application.Common.Mappers;
using Ecommerce.Application.Common.Logging;
using Ecommerce.Application.Common.Caching;

namespace Ecommerce.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ILogger<CategoryService> _logger;
    private readonly ICacheService _cache;

    public CategoryService(
    ICategoryRepository categoryRepository,
    ILogger<CategoryService> logger,
    ICacheService cache)
    {
        _categoryRepository = categoryRepository;
        _logger = logger;
        _cache = cache;
    }

    public async Task<PagedResult<CategoryResponseDto>> GetAllAsync(CategoryQuery query)
    {
        var version = await _cache.GetAsync<long?>(CacheKeysCategory.CategoryListVersion()) ?? 0;

        var cacheKey = CacheKeysCategory.CategoryList(query, version);

        var cached = await _cache.GetAsync<PagedResult<CategoryResponseDto>>(cacheKey);

        if (cached != null)
        {
            _logger.LogInformation(LogMessages.CategoryCacheHit, query);
            return cached;
        }

        _logger.LogInformation(LogMessages.CategoryCacheMiss, query);

        var categories = _categoryRepository.GetQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            categories = categories.Where(c => c.Name.StartsWith(query.Search));
        }

        categories = categories.ApplySorting(query.SortBy, query.SortOrder);

        var dtoQuery = categories.Select(c => new CategoryResponseDto
        {
            Id = c.Id,
            Name = c.Name,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        });

        var result = await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);

        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));

        await _cache.SetAsync(CacheKeysCategory.CategoryListVersion(), version, TimeSpan.FromDays(1));

        return result;
    }

    public async Task<CategoryResponseDto> GetByIdAsync(int id)
    {
        var cacheKey = CacheKeysCategory.Category(id);

        var cached = await _cache.GetAsync<CategoryResponseDto>(cacheKey);

        if (cached != null)
        {
            _logger.LogInformation(LogMessages.CategoryCacheHit, id);
            return cached;
        }

        _logger.LogInformation(LogMessages.CategoryCacheMiss, id);

        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            _logger.LogWarning(LogMessages.CategoryNotFound, id);
            throw new NotFoundException("Category not found");
        }

        var dto = CategoryMapper.ToDto(category);
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(10));

        return dto;
    }

    public async Task<CategoryResponseDto> CreateAsync(CreateCategoryDto dto)
    {
        _logger.LogInformation(LogMessages.CategoryCreating, dto.Name);

        var category = new Category
        {
            Name = dto.Name
        };

        await _categoryRepository.AddAsync(category);
        await _categoryRepository.SaveChangesAsync();

        await BumpCategoryListVersionAsync();

        return CategoryMapper.ToDto(category);
    }

    public async Task<CategoryResponseDto> UpdateAsync(int id, UpdateCategoryDto dto)
    {
        _logger.LogInformation(LogMessages.CategoryUpdating, id);

        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            _logger.LogWarning(LogMessages.CategoryNotFound, id);
            throw new NotFoundException("Category not found");
        }

        category.Name = dto.Name;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.SaveChangesAsync();

        await _cache.RemoveAsync(CacheKeysCategory.Category(id));
        await BumpCategoryListVersionAsync();

        return CategoryMapper.ToDto(category);
    }

    public async Task DeleteAsync(int id)
    {
        _logger.LogInformation(LogMessages.CategoryDeleting, id);

        var category = await _categoryRepository.GetByIdAsync(id);

        if (category == null)
        {
            _logger.LogWarning(LogMessages.CategoryNotFound, id);
            throw new NotFoundException("Category not found");
        }

        var hasProducts = await _categoryRepository.HasProductsAsync(id);

        if (hasProducts)
        {
            _logger.LogWarning(LogMessages.CategoryHasProducts, id);

            throw new BusinessException(
                "Cannot delete category because it has products.");
        }

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.SaveChangesAsync();

        await _cache.RemoveAsync(CacheKeysCategory.Category(id));
        await BumpCategoryListVersionAsync();
    }
    private async Task BumpCategoryListVersionAsync()
    {
        var version = await _cache.GetAsync<long?>(CacheKeysCategory.CategoryListVersion()) ?? 0;

        await _cache.SetAsync(
            CacheKeysCategory.CategoryListVersion(),
            version + 1,
            TimeSpan.FromDays(1));
    }
}
