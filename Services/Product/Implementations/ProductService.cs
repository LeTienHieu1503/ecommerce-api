using Microsoft.EntityFrameworkCore;
using Ecommerce.API.Data;
using Ecommerce.API.Models;
using Ecommerce.API.DTOs.Product;
using Ecommerce.API.Services.Product.Interfaces;
using Ecommerce.API.Exceptions;

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
        if (dto.Price <= 0)
            throw new ValidationException("Price must be greater than 0");

        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == dto.CategoryId);

        if (!categoryExists)
            throw new NotFoundException("Category not found");

        var product = new Ecommerce.API.Models.Product
        {
            Name = dto.Name,
            Price = dto.Price,
            CategoryId = dto.CategoryId
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return MapToDto(product);
    }

    public async Task<ProductResponseDto> GetByIdAsync(int id)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            throw new NotFoundException("Product not found");

        return MapToDto(product);
    }

    public async Task<List<ProductResponseDto>> GetAllAsync(int page, int pageSize)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 10;

        return await _context.Products
            .Include(p => p.Category)
            .Where(p => !p.IsDeleted)
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
            .ToListAsync();
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

        return MapToDto(product);
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

    private static ProductResponseDto MapToDto(Ecommerce.API.Models.Product product)
    {
        return new ProductResponseDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            CategoryId = product.CategoryId,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };
    }
}