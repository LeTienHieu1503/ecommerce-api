using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Common.Enums;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.DTOs.Order;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Application.Common.Caching;

namespace Ecommerce.Application.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IProductRepository _productRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepo,
        IProductRepository productRepo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<OrderService> logger)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        var correlationId = Guid.NewGuid().ToString();
        _logger.LogInformation("CreateOrder started | CorrelationId={CorrelationId} | UserId={UserId}",
            correlationId, request.UserId);

        if (request.Items == null || request.Items.Count == 0)
            throw new BusinessException("Order must contain at least one item.");

        // Group duplicate items to prevent multiple separate stock checks for the same product
        var groupedItems = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemRequest
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            var order = new Order
            {
                UserId = request.UserId,
                CreatedAt = DateTime.UtcNow,
                Status = OrderStatus.Pending,
                Items = new List<OrderItem>()
            };

            foreach (var itemRequest in groupedItems)
            {
                if (itemRequest.Quantity <= 0)
                    throw new BusinessException($"Quantity for Product {itemRequest.ProductId} must be greater than 0.");

                var product = await _productRepo.GetByIdAsync(itemRequest.ProductId);
                if (product == null)
                    throw new BusinessException($"Product {itemRequest.ProductId} not found.");

                if (product.Stock < itemRequest.Quantity)
                    throw new BusinessException($"Out of stock for Product '{product.Name}'. Available: {product.Stock}.");

                product.Stock -= itemRequest.Quantity;

                order.Items.Add(new OrderItem
                {
                    ProductId = itemRequest.ProductId,
                    Quantity = itemRequest.Quantity,
                    Price = product.Price
                });
            }

            await _orderRepo.AddAsync(order);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            // Invalidate product cache AFTER commit
            foreach (var item in order.Items)
            {
                try
                {
                    await _cache.RemoveAsync(CacheKeysProduct.Product(item.ProductId));
                    _logger.LogInformation("Cache invalidated | {CacheKey}", CacheKeysProduct.Product(item.ProductId));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to invalidate cache for Product {ProductId}", item.ProductId);
                }
            }

            // Bump product list version to invalidate all cached lists
            await BumpProductListVersionAsync();

            _logger.LogInformation("Order created successfully | OrderId={OrderId} | CorrelationId={CorrelationId}",
                order.Id, correlationId);

            return MapToDto(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _logger.LogError("Concurrency conflict detected during CreateOrder | CorrelationId={CorrelationId}", correlationId);
            throw new BusinessException("Inventory was updated by another process. Please retry.");
        }
        catch (BusinessException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "CreateOrder failed | CorrelationId={CorrelationId}", correlationId);
            throw;
        }
    }

    public async Task<OrderDto?> GetOrderByIdAsync(int id)
    {
        var order = await _orderRepo.GetByIdAsync(id);
        return order != null ? MapToDto(order) : null;
    }

    public async Task<IEnumerable<OrderDto>> GetOrdersByUserIdAsync(int userId)
    {
        var orders = await _orderRepo.GetByUserIdAsync(userId);
        return orders.Select(MapToDto);
    }

    public async Task<IEnumerable<OrderDto>> GetAllOrdersAsync()
    {
        var orders = await _orderRepo.GetAllAsync();
        return orders.Select(MapToDto);
    }

    public async Task CancelOrderAsync(int orderId)
    {
        _logger.LogInformation("CancelOrder started | OrderId={OrderId}", orderId);

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            var order = await _orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new BusinessException("Order not found.");

            if (order.Status != OrderStatus.Pending)
                throw new BusinessException($"Cannot cancel order with status '{order.Status}'. Only Pending orders can be cancelled.");

            order.Status = OrderStatus.Cancelled;

            foreach (var item in order.Items)
            {
                var product = await _productRepo.GetByIdAsync(item.ProductId);
                if (product != null)
                {
                    product.Stock += item.Quantity;
                }
                else
                {
                    _logger.LogWarning("Product {ProductId} not found during stock restore for OrderId={OrderId}",
                        item.ProductId, orderId);
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            foreach (var item in order.Items)
            {
                try
                {
                    await _cache.RemoveAsync(CacheKeysProduct.Product(item.ProductId));
                    _logger.LogInformation("Cache invalidated | {CacheKey}", CacheKeysProduct.Product(item.ProductId));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to invalidate cache for Product {ProductId}", item.ProductId);
                }
            }

            // Bump product list version to invalidate all cached lists
            await BumpProductListVersionAsync();

            _logger.LogInformation("Order cancelled successfully | OrderId={OrderId}", orderId);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _logger.LogError("Concurrency conflict during CancelOrder | OrderId={OrderId}", orderId);
            throw new BusinessException("Order or product was updated by another process. Please retry.");
        }
        catch (BusinessException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "CancelOrder failed | OrderId={OrderId}", orderId);
            throw;
        }
    }

    private async Task BumpProductListVersionAsync()
    {
        try
        {
            var version = await _cache.IncrementAsync(
                CacheKeysProduct.ProductListVersion(),
                TimeSpan.FromDays(1));
            _logger.LogInformation("Product list cache version bumped to {Version}", version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bump product list cache version");
        }
    }

    private static OrderDto MapToDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            UserId = order.UserId,
            CreatedAt = order.CreatedAt,
            Status = order.Status.ToString(),
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
