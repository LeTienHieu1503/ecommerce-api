using System.ComponentModel.DataAnnotations;

namespace Ecommerce.API.DTOs.Category;

public class CreateCategoryDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
}