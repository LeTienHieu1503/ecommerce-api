namespace Ecommerce.API.DTOs;

public class CreateProductDto
{
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
}
