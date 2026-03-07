using Ecommerce.API.DTOs.Category;

namespace Ecommerce.API.Services.Category.Interfaces;

public interface ICategoryService
{
    Task<List<CategoryResponseDto>> GetAllAsync();

    Task<CategoryResponseDto> GetByIdAsync(int id);

    Task<CategoryResponseDto> CreateAsync(CreateCategoryDto dto);

    Task<CategoryResponseDto> UpdateAsync(int id, UpdateCategoryDto dto);

    Task DeleteAsync(int id);
}