using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Application.Common.Sorting;
using Ecommerce.Application.DTOs.Product;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository productRepository,
        ILogger<ProductService> logger)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto dto)
    {
        _logger.LogInformation(
            "Creating product {ProductName} in category {CategoryId}",
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
            CategoryId = dto.CategoryId
        };

        await _productRepository.AddAsync(product);
        await _productRepository.SaveChangesAsync();


        return await GetByIdAsync(product.Id);
    }

    public async Task<ProductResponseDto> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting product {ProductId}", id);

        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
        {
            _logger.LogWarning("Product not found {ProductId}", id);
            throw new NotFoundException("Product not found");
        }

        return new ProductResponseDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            CategoryId = product.CategoryId,
            CategoryName = product.Category.Name,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };
    }

    public async Task<PagedResult<ProductResponseDto>> GetAllAsync(ProductQuery query)
    {
        var products = _productRepository.GetQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            products = products.Where(p => p.Name.StartsWith(query.Search));
        }

        if (query.CategoryId.HasValue)
        {
            products = products.Where(p => p.CategoryId == query.CategoryId);
        }

        if (query.MinPrice.HasValue)
        {
            products = products.Where(p => p.Price >= query.MinPrice);
        }

        if (query.MaxPrice.HasValue)
        {
            products = products.Where(p => p.Price <= query.MaxPrice);
        }

        products = query.SortBy?.ToLower() switch
        {
            "name" => query.SortOrder == "desc"
                ? products.OrderByDescending(p => p.Name)
                : products.OrderBy(p => p.Name),

            "price" => query.SortOrder == "desc"
                ? products.OrderByDescending(p => p.Price)
                : products.OrderBy(p => p.Price),

            _ => products.OrderBy(p => p.Id)
        };

        var dtoQuery = products.Select(p => new ProductResponseDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            CategoryId = p.CategoryId,
            CategoryName = p.Category.Name,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        });

        return await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);
    }

    public async Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto dto)
    {
        _logger.LogInformation("Updating product {ProductId}", id);

        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
            throw new NotFoundException("Product not found");

        var categoryExists = await _productRepository.CategoryExistsAsync(dto.CategoryId);

        if (!categoryExists)
            throw new NotFoundException("Category not found");

        product.Name = dto.Name;
        product.Price = dto.Price;
        product.CategoryId = dto.CategoryId;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.SaveChangesAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task DeleteAsync(int id)
    {
        _logger.LogInformation("Soft deleting product {ProductId}", id);

        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
            throw new NotFoundException("Product not found");

        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.SaveChangesAsync();
    }
}