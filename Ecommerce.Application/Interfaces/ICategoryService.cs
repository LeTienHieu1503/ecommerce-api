using Ecommerce.Application.DTOs.Category;
using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.Interfaces;

public interface ICategoryService
{
    Task<PagedResult<CategoryResponseDto>> GetAllAsync(CategoryQuery query);
    Task<CategoryResponseDto> GetByIdAsync(int id);
    Task<CategoryResponseDto> CreateAsync(CreateCategoryDto dto);
    Task<CategoryResponseDto> UpdateAsync(int id, UpdateCategoryDto dto);
    Task DeleteAsync(int id);
}
