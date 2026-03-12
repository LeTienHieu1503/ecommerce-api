using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Category;

public class UpdateCategoryDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
}
