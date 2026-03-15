using Ecommerce.Domain.Entities;
using Ecommerce.Application.DTOs.Category;

namespace Ecommerce.Application.Common.Mappers;

public static class CategoryMapper
{
    public static CategoryResponseDto ToDto(Category category)
    {
        return new CategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }
}