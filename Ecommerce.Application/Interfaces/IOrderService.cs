using Ecommerce.Application.DTOs.Order;
using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request);

    Task<OrderDto?> GetOrderByIdAsync(int id);

    Task<PagedResult<OrderDto>> GetAllOrdersAsync(OrderQuery query);

    Task<IEnumerable<OrderDto>> GetOrdersForCurrentUserAsync();

    Task<IEnumerable<OrderDto>> GetOrdersByUserIdAsync(int userId);
    
    Task CancelOrderAsync(int orderId, int currentUserId, bool canCancelAnyOrder = false);

    Task<CheckoutResponseDto> CreateCheckoutAsync(int orderId, int userId);

    Task<OrderDto> AddOrderFromCartAsync(int userId);

    Task HandlePaymentSucceededAsync(string paymentIntentId);

    Task HandlePaymentFailedAsync(string paymentIntentId);

    Task<OrderDto> RefundPaidOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task HandleRefundCompletedAsync(string paymentIntentId, string? stripeRefundId, CancellationToken cancellationToken = default);
}
