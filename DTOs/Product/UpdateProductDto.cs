using System.ComponentModel.DataAnnotations;

namespace Ecommerce.API.DTOs.Product;

public class UpdateProductDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }
}