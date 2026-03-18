using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Category;

public class CreateCategoryDto
{
    [Required(ErrorMessage = "Name is Required")]
    public string Name { get; set; } = string.Empty;
}
