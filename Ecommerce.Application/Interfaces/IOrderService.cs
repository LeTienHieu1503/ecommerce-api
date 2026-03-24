using Ecommerce.Application.DTOs.Order;
using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request);
    Task<OrderDto?> GetOrderByIdAsync(int id);
    Task<PagedResult<OrderDto>> GetAllOrdersAsync(OrderQuery query);
    Task<IEnumerable<OrderDto>> GetOrdersByUserIdAsync(int userId);
    /// <param name="canCancelAnyOrder">When true (e.g. Admin), ownership is not enforced.</param>
    Task CancelOrderAsync(int orderId, int currentUserId, bool canCancelAnyOrder = false);
}
