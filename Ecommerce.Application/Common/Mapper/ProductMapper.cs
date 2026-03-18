using Ecommerce.Domain.Entities;
using Ecommerce.Application.DTOs.Product;

namespace Ecommerce.Application.Common.Mappers;

public static class ProductMapper
{
    public static ProductResponseDto ToDto(Product product)
    {
        return new ProductResponseDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            Stock = product.Stock,
            RowVersion = product.RowVersion,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };
    }
}
