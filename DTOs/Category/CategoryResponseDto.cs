namespace Ecommerce.API.DTOs.Category;

public class CategoryResponseDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}