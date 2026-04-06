using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Cart;

public class UpdateCartItemRequest
{
    [Required]
    [Range(0, 100, ErrorMessage = "Quantity must be between 0 and 100 (0 removes the item)")]
    public int Quantity { get; set; }
}