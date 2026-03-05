using System.ComponentModel.DataAnnotations;

namespace Ecommerce.API.DTOs.Product;

public class CreateProductDto
{
    [Required(ErrorMessage = "Name is required")] 
    [MaxLength(200, ErrorMessage = "Name cannot exceed 200 characters")]
    public string Name { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "Price must be than 0")]
    public decimal Price { get; set; }
}