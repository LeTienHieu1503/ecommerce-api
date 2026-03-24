using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Product;

public class UpdateProductDto
{
    [Required(ErrorMessage = "Name is Required")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "CategoryId is Required")]
    public int CategoryId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Stock cannot be negative")]
    public int Stock { get; set; }
}
