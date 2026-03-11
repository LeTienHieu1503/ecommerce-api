using Ecommerce.API.DTOs.Product;
using Ecommerce.API.Common.Pagination;

namespace Ecommerce.API.Services.Product.Interfaces;

public interface IProductService
{
    Task<ProductResponseDto> CreateAsync(CreateProductDto dto);

    Task<ProductResponseDto> GetByIdAsync(int id);

    Task<PagedResult<ProductResponseDto>> GetAllAsync(ProductQuery query);

    Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto dto);

    Task DeleteAsync(int id);
}