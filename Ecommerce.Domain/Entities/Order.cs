using Ecommerce.Domain.Common.Enums;

namespace Ecommerce.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public List<OrderItem> Items { get; set; } = new();

    // Payment
    public string? StripePaymentIntentId { get; set; }  // "pi_xxx"
    public string? StripeRefundId { get; set; }         // "re_xxx"
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
}
