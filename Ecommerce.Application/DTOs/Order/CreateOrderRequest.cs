using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ecommerce.Application.DTOs.Order;

public class CreateOrderRequest
{
    [JsonIgnore]
    public int UserId { get; set; }

    [Required(ErrorMessage = "Order must contain at least one item")]
    [MinLength(1, ErrorMessage = "Order must contain at least one item")]
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "ProductId is required")]
    public int ProductId { get; set; }

    [Required]
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000")]
    public int Quantity { get; set; }
}
