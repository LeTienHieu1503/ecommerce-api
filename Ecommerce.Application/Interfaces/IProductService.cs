using Ecommerce.Application.DTOs.Product;
using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.Interfaces;

public interface IProductService
{
    Task<ProductResponseDto> CreateAsync(CreateProductDto dto);
    Task<ProductResponseDto> GetByIdAsync(int id);
    Task<PagedResult<ProductResponseDto>> GetAllAsync(ProductQuery query);
    Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto dto);
    Task DeleteAsync(int id);
}
