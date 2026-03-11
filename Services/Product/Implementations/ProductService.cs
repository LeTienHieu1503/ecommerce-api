using Ecommerce.API.Common.Pagination;
using Ecommerce.API.Common.Sorting;
using Ecommerce.API.Data;
using Ecommerce.API.DTOs.Product;
using Ecommerce.API.Exceptions;
using Ecommerce.API.Services.Product.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.API.Services.Product.Implementations;

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _context;

    public ProductService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto dto)
    {
        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == dto.CategoryId);

        if (!categoryExists)
            throw new NotFoundException("Category not found");

        var product = new Models.Product
        {
            Name = dto.Name,
            Price = dto.Price,
            CategoryId = dto.CategoryId
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task<ProductResponseDto> GetByIdAsync(int id)
    {
        var product = await _context.Products
            .Where(p => p.Id == id && !p.IsDeleted)
            .Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (product == null)
            throw new NotFoundException("Product not found");

        return product;
    }

    //public async Task<PagedResult<ProductResponseDto>> GetAllAsync(PaginationParams pagination)
    //{
    //    var query = _context.Products
    //        .Where(p => !p.IsDeleted)
    //        .OrderBy(p => p.Id)
    //        .Select(p => new ProductResponseDto
    //        {
    //            Id = p.Id,
    //            Name = p.Name,
    //            Price = p.Price,
    //            CategoryId = p.CategoryId,
    //            CategoryName = p.Category.Name,
    //            CreatedAt = p.CreatedAt,
    //            UpdatedAt = p.UpdatedAt
    //        })
    //        .AsNoTracking();

    //    return await query.ToPagedResultAsync(pagination.Page, pagination.PageSize);
    //}
    public async Task<PagedResult<ProductResponseDto>> GetAllAsync(ProductQuery query)
    {
        var products = _context.Products
            .Where(p => !p.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            products = products.Where(p => p.Name.Contains(query.Search));
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

        //products = products.ApplySorting(query.SortBy, query.SortOrder);

        var dtoQuery = products
            .Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .AsNoTracking();

        return await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);
    }

    public async Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto dto)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (product == null)
            throw new NotFoundException("Product not found");

        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == dto.CategoryId);

        if (!categoryExists)
            throw new NotFoundException("Category not found");

        product.Name = dto.Name;
        product.Price = dto.Price;
        product.CategoryId = dto.CategoryId;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (product == null)
            throw new NotFoundException("Product not found");

        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }
}