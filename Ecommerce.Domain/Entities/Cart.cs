namespace Ecommerce.Domain.Entities;

public class Cart
{
    public int UserId { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public decimal TotalAmount => Items.Sum(i => i.TotalPrice);
    public DateTime LastUpdatedAt { get; set; }
}