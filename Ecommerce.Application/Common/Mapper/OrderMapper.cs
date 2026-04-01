using Ecommerce.Domain.Entities;
using Ecommerce.Application.DTOs.Order;

namespace Ecommerce.Application.Common.Mappers;

public static class OrderMapper
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            UserId = order.UserId,
            CreatedAt = order.CreatedAt,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            StripePaymentIntentId = order.StripePaymentIntentId,
            StripeRefundId = order.StripeRefundId,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList()
        };
    }
}