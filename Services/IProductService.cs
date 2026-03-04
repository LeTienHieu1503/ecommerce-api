using Ecommerce.API.DTOs;

namespace Ecommerce.API.Services;

public interface IProductService
{
    Task<ProductResponseDto> CreateAsync(CreateProductDto dto);
    Task<ProductResponseDto?> GetByIdAsync(int id);
    Task<IEnumerable<ProductResponseDto>> GetAllAsync();
    Task<bool> UpdateAsync(int id, UpdateProductDto dto);
    Task<bool> DeleteAsync(int id);
}
