using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Product;

public class CreateProductDto
{
    [Required(ErrorMessage = "Name is required")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "CategoryId is Required")]
    public int CategoryId { get; set; }
}
