using Ecommerce.Application.DTOs.Order;

namespace Ecommerce.Application.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request);
    Task<OrderDto?> GetOrderByIdAsync(int id);
    Task<IEnumerable<OrderDto>> GetAllOrdersAsync();
    Task<IEnumerable<OrderDto>> GetOrdersByUserIdAsync(int userId);
    Task CancelOrderAsync(int orderId);
}
