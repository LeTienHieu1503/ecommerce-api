using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Application.Common.Sorting;
using Ecommerce.Application.DTOs.Product;
using Ecommerce.Application.Common.Mappers;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Application.Common.Caching;
using Ecommerce.Application.Common.Logging;

namespace Ecommerce.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<ProductService> _logger;
    private readonly ICacheService _cache;

    private static readonly SemaphoreSlim _productListLock = new SemaphoreSlim(1, 1);

    public ProductService(
        IProductRepository productRepository,
        ILogger<ProductService> logger,
        ICacheService cache)
    {
        _productRepository = productRepository;
        _logger = logger;
        _cache = cache;
    }

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto dto)
    {
        _logger.LogInformation(
            LogMessages.ProductCreating,
            dto.Name,
            dto.CategoryId);

        var categoryExists = await _productRepository.CategoryExistsAsync(dto.CategoryId);

        if (!categoryExists)
        {
            _logger.LogWarning("Category not found: {CategoryId}", dto.CategoryId);
            throw new NotFoundException("Category not found");
        }
        var product = new Product
        {
            Name = dto.Name,
            Price = dto.Price,
            CategoryId = dto.CategoryId,
            Stock = dto.Stock
        };

        await _productRepository.AddAsync(product);
        await _productRepository.SaveChangesAsync();

        await BumpProductListVersionAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task<ProductResponseDto> GetByIdAsync(int id)
    {
        var cacheKey = CacheKeysProduct.Product(id);

        var cached = await _cache.GetAsync<ProductResponseDto>(cacheKey);

        if (cached != null)
        {
            _logger.LogInformation(LogMessages.ProductCacheHit, id);
            return cached;
        }

        _logger.LogInformation(LogMessages.ProductCacheMiss, id);

        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
        {
            _logger.LogWarning(LogMessages.ProductNotFound, id);
            throw new NotFoundException("Product not found");
        }

        var response = ProductMapper.ToDto(product);

        await _cache.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));

        return response;
    }

    public async Task<PagedResult<ProductResponseDto>> GetAllAsync(ProductQuery query)
    {
        var version = await _cache.GetAsync<long?>(CacheKeysProduct.ProductListVersion()) ?? 0;
        var cacheKey = CacheKeysProduct.ProductList(query, version);

        var cached = await _cache.GetAsync<PagedResult<ProductResponseDto>>(cacheKey);
        if (cached != null)
        {
            _logger.LogInformation(LogMessages.ProductCacheHit, query);
            return cached;
        }

        await _productListLock.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cached = await _cache.GetAsync<PagedResult<ProductResponseDto>>(cacheKey);
            if (cached != null)
            {
                _logger.LogInformation(LogMessages.ProductCacheHit, query);
                return cached;
            }

            _logger.LogInformation(LogMessages.ProductCacheMiss, query);

            var products = _productRepository.GetQueryable();

            if (!string.IsNullOrWhiteSpace(query.Search))
                products = products.Where(p => p.Name.StartsWith(query.Search));

            if (query.CategoryId.HasValue)
                products = products.Where(p => p.CategoryId == query.CategoryId.Value);

            if (query.MinPrice.HasValue)
                products = products.Where(p => p.Price >= query.MinPrice);

            if (query.MaxPrice.HasValue)
                products = products.Where(p => p.Price <= query.MaxPrice);

            products = products.ApplySorting(query.SortBy, query.SortOrder);

            var dtoQuery = products.Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,
                Stock = p.Stock,
                RowVersion = p.RowVersion,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            });

            var result = await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(2));
            return result;
        }
        finally
        {
            _productListLock.Release();
        }
    }

    private async Task BumpProductListVersionAsync()
    {
        try
        {
            await _cache.IncrementAsync(
                CacheKeysProduct.ProductListVersion(),
                TimeSpan.FromDays(1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bump product list cache version");
        }
    }

    public async Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto dto)
    {
        _logger.LogInformation(LogMessages.ProductUpdating, id);

        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
            throw new NotFoundException("Product not found");

        var categoryExists = await _productRepository.CategoryExistsAsync(dto.CategoryId);

        if (!categoryExists)
            throw new NotFoundException("Category not found");

        product.Name = dto.Name;
        product.Price = dto.Price;
        product.CategoryId = dto.CategoryId;
        product.Stock = dto.Stock;
        product.UpdatedAt = DateTime.UtcNow;

        // Apply concurrency token
        _productRepository.UpdateConcurrencyToken(product, dto.RowVersion);

        try
        {
            await _productRepository.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogError("Concurrency conflict during product update. ProductId={ProductId}", id);
            throw new BusinessException("The product was updated by another user. Please refresh and try again.");
        }

        await _cache.RemoveAsync(CacheKeysProduct.Product(id));
        await BumpProductListVersionAsync();

        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(int id)
    {
        _logger.LogInformation(LogMessages.ProductDeleting, id);

        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
            throw new NotFoundException("Product not found");

        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.SaveChangesAsync();

        await _cache.RemoveAsync(CacheKeysProduct.Product(id));
        await BumpProductListVersionAsync();
    }
}
