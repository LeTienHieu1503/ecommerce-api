using Ecommerce.API.DTOs.Product;

namespace Ecommerce.API.Services.Product.Interfaces;

public interface IProductService
{
    Task<ProductResponseDto> CreateAsync(CreateProductDto dto);

    Task<ProductResponseDto> GetByIdAsync(int id);

    Task<List<ProductResponseDto>> GetAllAsync(int page, int pageSize);

    Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto dto);

    Task DeleteAsync(int id);
}