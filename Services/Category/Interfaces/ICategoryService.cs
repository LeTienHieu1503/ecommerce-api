using Ecommerce.API.DTOs.Category;
using Ecommerce.API.Common.Pagination;

namespace Ecommerce.API.Services.Category.Interfaces;

public interface ICategoryService
{
    Task<PagedResult<CategoryResponseDto>> GetAllAsync(CategoryQuery query);

    Task<CategoryResponseDto> GetByIdAsync(int id);

    Task<CategoryResponseDto> CreateAsync(CreateCategoryDto dto);

    Task<CategoryResponseDto> UpdateAsync(int id, UpdateCategoryDto dto);

    Task DeleteAsync(int id);
}