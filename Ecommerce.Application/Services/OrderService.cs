using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Common.Enums;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.DTOs.Order;
using Ecommerce.Application.Common.Sorting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Application.Common.Caching;
using Ecommerce.Application.Common.Http;
using System.Collections.Concurrent;
using Ecommerce.Application.Common.Mappers;

namespace Ecommerce.Application.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IProductRepository _productRepo;
    private readonly ICartService _cartService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<OrderService> _logger;
    private readonly IRequestDeviceContext _requestDeviceContext;

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderByIdLocks = new();

    public OrderService(
        IOrderRepository orderRepo,
        IProductRepository productRepo,
        ICartService cartService,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IPaymentService paymentService,
        ILogger<OrderService> logger,
        IRequestDeviceContext requestDeviceContext)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _cartService = cartService;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _paymentService = paymentService;
        _logger = logger;
        _requestDeviceContext = requestDeviceContext;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(CreateOrderAsync));
        var correlationId = Guid.NewGuid().ToString();
        _logger.LogInformation("CreateOrder started | CorrelationId={CorrelationId} | UserId={UserId}",
            correlationId, request.UserId);

        if (request.Items == null || request.Items.Count == 0)
            throw new BusinessException("Order must contain at least one item.");

        var invalidItem = request.Items.FirstOrDefault(x => x.Quantity <= 0);
        if (invalidItem != null)
            throw new BusinessException($"Quantity for Product {invalidItem.ProductId} must be greater than 0.");

        var groupedItems = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemRequest
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        var invalidGrouped = groupedItems.FirstOrDefault(i => i.Quantity <= 0);
        if (invalidGrouped != null)
            throw new BusinessException($"Total quantity for Product {invalidGrouped.ProductId} after merge must be greater than 0. Check duplicate order items.");

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
                var product = await _productRepo.GetByIdAsync(itemRequest.ProductId);
                if (product == null)
                    throw new BusinessException($"Product {itemRequest.ProductId} not found.");

                var maxOrderQuantity = product.Stock;
                if (itemRequest.Quantity > maxOrderQuantity)
                {
                    throw new BusinessException(
                        $"Quantity for product '{product.Name}' cannot exceed available stock. Maximum: {maxOrderQuantity}, requested: {itemRequest.Quantity}.");
                }

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

            await BumpProductListVersionAsync();

            _logger.LogInformation("Order created successfully | OrderId={OrderId} | CorrelationId={CorrelationId}",
                order.Id, correlationId);

            return OrderMapper.ToDto(order);
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
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(GetOrderByIdAsync));
        var cacheKey = CacheKeysOrder.Order(id);

        var cached = await _cache.GetAsync<OrderDto>(cacheKey);
        if (cached != null)
        {
            _logger.LogInformation("Cache hit for Order {OrderId}", id);
            return cached;
        }

        var keyLock = _orderByIdLocks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));
        var acquired = await keyLock.WaitAsync(TimeSpan.FromSeconds(5));
        if (!acquired)
            throw new TimeoutException($"Could not acquire cache lock for Order {id}.");

        try
        {
            cached = await _cache.GetAsync<OrderDto>(cacheKey);
            if (cached != null)
            {
                _logger.LogInformation("Cache hit for Order {OrderId}", id);
                return cached;
            }

            _logger.LogInformation("Cache miss for Order {OrderId}", id);

            var order = await _orderRepo.GetByIdAsync(id);
            if (order == null)
                return null;

            var dto = OrderMapper.ToDto(order);
            await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(2));
            return dto;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task<IEnumerable<OrderDto>> GetOrdersForCurrentUserAsync()
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(GetOrdersForCurrentUserAsync));
        var userId = _requestDeviceContext.UserId
            ?? throw new UnauthorizedException("User identifier is not available.");

        var orders = await _orderRepo.GetByUserIdAsync(userId);
        return orders.Select(o => OrderMapper.ToDto(o));
    }

    public async Task<IEnumerable<OrderDto>> GetOrdersByUserIdAsync(int userId)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(GetOrdersByUserIdAsync));
        var orders = await _orderRepo.GetByUserIdAsync(userId);
        return orders.Select(o => OrderMapper.ToDto(o));
    }

    public async Task<PagedResult<OrderDto>> GetAllOrdersAsync(OrderQuery query)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(GetAllOrdersAsync));
        var orders = _orderRepo.GetQueryable();

        if (query.Status.HasValue)
            orders = orders.Where(o => o.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            if (int.TryParse(term, out var n))
                orders = orders.Where(o => o.Id == n || o.UserId == n);
            else if (Enum.TryParse<OrderStatus>(term, true, out var st))
                orders = orders.Where(o => o.Status == st);
            else
                orders = orders.Where(o => o.Status.ToString().Contains(term));
        }

        var sortField = NormalizeOrderSortField(query.SortBy);
        if (sortField != null)
            orders = orders.ApplySorting(sortField, query.SortOrder);
        else
            orders = orders.OrderByDescending(o => o.CreatedAt);

        var dtoQuery = orders.Select(o => new OrderDto
        {
            Id = o.Id,
            UserId = o.UserId,
            CreatedAt = o.CreatedAt,
            Status = o.Status.ToString(),
            PaymentStatus = o.PaymentStatus.ToString(),
            StripePaymentIntentId = o.StripePaymentIntentId,
            StripeRefundId = o.StripeRefundId,
            Items = o.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList()
        });

        return await dtoQuery.ToPagedResultAsync(query.Page, query.PageSize);
    }

    private static string? NormalizeOrderSortField(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return null;

        return sortBy.Trim().ToLowerInvariant() switch
        {
            "id" => "Id",
            "userid" => "UserId",
            "createdat" => "CreatedAt",
            "status" => "Status",
            _ => null
        };
    }

    public async Task CancelOrderAsync(int orderId, int currentUserId, bool canCancelAnyOrder = false)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(CancelOrderAsync));
        _logger.LogInformation(
            "CancelOrder started | OrderId={OrderId}, UserId={UserId}, CanCancelAnyOrder={CanCancelAny}",
            orderId, currentUserId, canCancelAnyOrder);

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            var order = await _orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Order {orderId} not found.");

            if (!canCancelAnyOrder && order.UserId != currentUserId)
            {
                _logger.LogWarning(
                    "User {UserId} unauthorized to cancel Order {OrderId} (owner={OwnerId})",
                    currentUserId, orderId, order.UserId);
                throw new ForbiddenException("You are not authorized to cancel this order.");
            }

            if (order.Status != OrderStatus.Pending)
                throw new BusinessException($"Cannot cancel order with status '{order.Status}'. Only Pending orders can be cancelled.");

            order.Status = OrderStatus.Cancelled;
            order.PaymentStatus = PaymentStatus.Cancelled;

            await RestoreStockForOrderItemsAsync(order, orderId);

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            await InvalidateOrderAndProductCachesAsync(order, orderId);

            _logger.LogInformation("Order cancelled successfully | OrderId={OrderId}, UserId={UserId}", orderId, currentUserId);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _logger.LogError("Concurrency conflict during CancelOrder | OrderId={OrderId}, UserId={UserId}", orderId, currentUserId);
            throw new BusinessException("Order was updated by another process. Please retry.");
        }
        catch (NotFoundException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (ForbiddenException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (BusinessException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "CancelOrder failed | OrderId={OrderId}, UserId={UserId}", orderId, currentUserId);
            throw;
        }
    }

    public async Task<CheckoutResponseDto> CreateCheckoutAsync(int orderId, int userId)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(CreateCheckoutAsync));
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null || order.UserId != userId)
            throw new NotFoundException("Order not found");

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("Only pending orders can be PaymentCompleted.");

        if (order.PaymentStatus is not PaymentStatus.Pending and not PaymentStatus.Failed)
            throw new BusinessException("Order is not in a payable state.");

        var total = order.Items.Sum(i => i.Price * i.Quantity);
        if (total <= 0)
            throw new BusinessException("Order total must be greater than zero.");

        var amountInCents = (long)Math.Round(total * 100m, MidpointRounding.AwayFromZero);
        const string checkoutCurrency = "usd";

        if (order.PaymentStatus == PaymentStatus.Pending
            && !string.IsNullOrEmpty(order.StripePaymentIntentId))
        {
            var reuse = await _paymentService.GetReusablePaymentIntentAsync(
                order.StripePaymentIntentId, amountInCents, checkoutCurrency);
            if (reuse != null)
            {
                try
                {
                    await _cache.RemoveAsync(CacheKeysOrder.Order(orderId));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to invalidate cache for Order {OrderId} on checkout reuse", orderId);
                }

                return new CheckoutResponseDto { ClientSecret = reuse.ClientSecret };
            }
        }

        // Include amount + version in key so Stripe idempotency never clashes with older API payloads or changed totals.
        var idempotencyKey = order.PaymentStatus == PaymentStatus.Failed
            ? $"order-{orderId}-retry-{Guid.NewGuid():N}"
            : $"order-{orderId}-usd{amountInCents}-v2";

        var intent = await _paymentService
            .CreatePaymentIntentAsync(amountInCents, checkoutCurrency, orderId.ToString(), idempotencyKey);

        order.StripePaymentIntentId = intent.PaymentIntentId;

        await _unitOfWork.SaveChangesAsync();

        try
        {
            await _cache.RemoveAsync(CacheKeysOrder.Order(orderId));
        }
        catch (Exception cacheEx)
        {
            _logger.LogWarning(cacheEx, "Failed to invalidate cache for Order {OrderId} after checkout start", orderId);
        }

        return new CheckoutResponseDto { ClientSecret = intent.ClientSecret };
    }

    public async Task<OrderDto> AddOrderFromCartAsync(int userId)
    {
        var cart = await _cartService.GetCartAsync(userId);

        if (!cart.Items.Any())
            throw new BusinessException("Cart is empty");

        var createRequest = new CreateOrderRequest
        {
            UserId = userId,
            Items = cart.Items.Select(i => new OrderItemRequest
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity
            }).ToList()
        };

        var order = await CreateOrderAsync(createRequest);

        try
        {
            await _cartService.ClearCartAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Order {OrderId} created but failed to clear cart for user {UserId}",
                order.Id, userId);
        }

        return order;
    }

    public async Task HandlePaymentSucceededAsync(string paymentIntentId)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(HandlePaymentSucceededAsync));
        if (string.IsNullOrEmpty(paymentIntentId))
        {
            _logger.LogWarning("HandlePaymentSucceededAsync called with empty paymentIntentId");
            return;
        }

        var order = await _orderRepo.GetByStripePaymentIntentIdAsync(paymentIntentId);
        if (order == null)
        {
            _logger.LogWarning("No order found for PaymentIntent {PaymentIntentId}", paymentIntentId);
            return;
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            _logger.LogWarning(
                "Ignoring payment_intent.succeeded for refunded Order {OrderId}, PaymentIntent {PaymentIntentId}",
                order.Id, paymentIntentId);
            return;
        }

        if (order.Status == OrderStatus.Paid && order.PaymentStatus == PaymentStatus.Succeeded)
            return;

        if (order.StripePaymentIntentId != paymentIntentId)
            return;

        if (order.Status == OrderStatus.Cancelled || order.PaymentStatus == PaymentStatus.Cancelled)
        {
            _logger.LogWarning(
                "Ignoring payment_intent.succeeded for cancelled Order {OrderId}, PaymentIntent {PaymentIntentId}",
                order.Id, paymentIntentId);
            return;
        }

        order.Status = OrderStatus.Paid;
        order.PaymentStatus = PaymentStatus.Succeeded;

        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation(
            "Order {OrderId} marked Paid after PaymentIntent {PaymentIntentId}",
            order.Id, paymentIntentId);

        try
        {
            await _cache.RemoveAsync(CacheKeysOrder.Order(order.Id));
        }
        catch (Exception cacheEx)
        {
            _logger.LogWarning(cacheEx, "Failed to invalidate cache for Order {OrderId} after payment success", order.Id);
        }
    }

    public async Task HandlePaymentFailedAsync(string paymentIntentId)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(HandlePaymentFailedAsync));
        if (string.IsNullOrEmpty(paymentIntentId))
        {
            _logger.LogWarning("HandlePaymentFailedAsync called with empty paymentIntentId");
            return;
        }

        var order = await _orderRepo.GetByStripePaymentIntentIdAsync(paymentIntentId);
        if (order == null)
        {
            _logger.LogWarning("No order found for failed PaymentIntent {PaymentIntentId}", paymentIntentId);
            return;
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
            return;

        if (order.PaymentStatus == PaymentStatus.Succeeded || order.Status == OrderStatus.Paid)
            return;

        if (order.Status == OrderStatus.Cancelled || order.PaymentStatus == PaymentStatus.Cancelled)
            return;

        order.PaymentStatus = PaymentStatus.Failed;
        await _unitOfWork.SaveChangesAsync();

        try
        {
            await _cache.RemoveAsync(CacheKeysOrder.Order(order.Id));
        }
        catch (Exception cacheEx)
        {
            _logger.LogWarning(cacheEx, "Failed to invalidate cache for Order {OrderId} after payment failure", order.Id);
        }
    }

    public async Task<OrderDto> RefundPaidOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(RefundPaidOrderAsync));
        _logger.LogInformation("RefundPaidOrder started | OrderId={OrderId}", orderId);

        var preview = await _orderRepo.GetByIdAsync(orderId);
        if (preview == null)
            throw new NotFoundException($"Order {orderId} not found.");

        if (preview.PaymentStatus == PaymentStatus.Refunded)
            return OrderMapper.ToDto(preview);

        EnsureEligibleForRefund(preview, orderId);

        var refundResult = await _paymentService.CreateRefundForPaymentIntentAsync(
            preview.StripePaymentIntentId!,
            $"refund-order-{orderId}",
            cancellationToken);

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var order = await _orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Order {orderId} not found.");

            if (order.PaymentStatus == PaymentStatus.Refunded)
            {
                await transaction.CommitAsync();
                await InvalidateOrderAndProductCachesAsync(order, orderId);
                return OrderMapper.ToDto(order);
            }

            EnsureEligibleForRefund(order, orderId);

            ApplyRefundState(order, refundResult.RefundId);
            await RestoreStockForOrderItemsAsync(order, orderId);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync();

            await InvalidateOrderAndProductCachesAsync(order, orderId);

            _logger.LogInformation("Order {OrderId} refunded via API", orderId);
            return OrderMapper.ToDto(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _logger.LogError("Concurrency conflict during RefundPaidOrder | OrderId={OrderId}", orderId);
            throw new BusinessException("Order was updated by another process. Please retry.");
        }
        catch (NotFoundException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (BusinessException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "RefundPaidOrder failed | OrderId={OrderId}", orderId);
            throw;
        }
    }

    public async Task HandleRefundCompletedAsync(
        string paymentIntentId,
        string? stripeRefundId,
        CancellationToken cancellationToken = default)
    {
        RequestDeviceDiagnostics.Log(_logger, _requestDeviceContext, nameof(OrderService), nameof(HandleRefundCompletedAsync));
        if (string.IsNullOrEmpty(paymentIntentId))
        {
            _logger.LogWarning("HandleRefundCompletedAsync called with empty paymentIntentId");
            return;
        }

        var order = await _orderRepo.GetByStripePaymentIntentIdAsync(paymentIntentId);
        if (order == null)
        {
            _logger.LogWarning("No order found for refund webhook PaymentIntent {PaymentIntentId}", paymentIntentId);
            return;
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            if (!string.IsNullOrEmpty(stripeRefundId) && string.IsNullOrEmpty(order.StripeRefundId))
            {
                order.StripeRefundId = stripeRefundId;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                try
                {
                    await _cache.RemoveAsync(CacheKeysOrder.Order(order.Id));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to invalidate cache for Order {OrderId} after refund id patch", order.Id);
                }
            }

            return;
        }

        if (order.Status != OrderStatus.Paid || order.PaymentStatus != PaymentStatus.Succeeded)
        {
            _logger.LogInformation(
                "Skipping refund reconcile for Order {OrderId}: Status={Status}, PaymentStatus={PaymentStatus}",
                order.Id, order.Status, order.PaymentStatus);
            return;
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var locked = await _orderRepo.GetByStripePaymentIntentIdAsync(paymentIntentId);
            if (locked == null)
            {
                await transaction.RollbackAsync();
                return;
            }

            if (locked.PaymentStatus == PaymentStatus.Refunded)
            {
                await transaction.CommitAsync();
                return;
            }

            if (locked.Status != OrderStatus.Paid || locked.PaymentStatus != PaymentStatus.Succeeded)
            {
                await transaction.RollbackAsync();
                return;
            }

            ApplyRefundState(locked, stripeRefundId);
            await RestoreStockForOrderItemsAsync(locked, locked.Id);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync();

            await InvalidateOrderAndProductCachesAsync(locked, locked.Id);

            _logger.LogInformation(
                "Order {OrderId} marked refunded from webhook for PaymentIntent {PaymentIntentId}",
                locked.Id, paymentIntentId);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _logger.LogError("Concurrency conflict during HandleRefundCompleted | PaymentIntent={PaymentIntentId}", paymentIntentId);
            throw new BusinessException("Order was updated by another process. Please retry.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "HandleRefundCompleted failed | PaymentIntent={PaymentIntentId}", paymentIntentId);
            throw;
        }
    }

    private static void EnsureEligibleForRefund(Order order, int orderId)
    {
        if (order.Status != OrderStatus.Paid)
        {
            throw new BusinessException(
                $"Only paid orders can be refunded. Order {orderId} has status '{order.Status}'.");
        }

        if (order.PaymentStatus != PaymentStatus.Succeeded)
        {
            throw new BusinessException(
                $"Order {orderId} is not in a refundable payment state (payment status: '{order.PaymentStatus}').");
        }

        if (string.IsNullOrEmpty(order.StripePaymentIntentId))
            throw new BusinessException($"Order {orderId} has no Stripe payment intent to refund.");
    }

    private static void ApplyRefundState(Order order, string? stripeRefundId)
    {
        order.Status = OrderStatus.Cancelled;
        order.PaymentStatus = PaymentStatus.Refunded;
        if (!string.IsNullOrEmpty(stripeRefundId))
            order.StripeRefundId = stripeRefundId;
    }

    private async Task RestoreStockForOrderItemsAsync(Order order, int orderId)
    {
        foreach (var item in order.Items)
        {
            var product = await _productRepo.GetByIdAsync(item.ProductId);
            if (product != null)
                product.Stock += item.Quantity;
            else
            {
                _logger.LogWarning("Product {ProductId} not found during stock restore for OrderId={OrderId}",
                    item.ProductId, orderId);
            }
        }
    }

    private async Task InvalidateOrderAndProductCachesAsync(Order order, int orderId)
    {
        try
        {
            await _cache.RemoveAsync(CacheKeysOrder.Order(orderId));
            _logger.LogInformation("Cache invalidated | {CacheKey}", CacheKeysOrder.Order(orderId));
        }
        catch (Exception cacheEx)
        {
            _logger.LogWarning(cacheEx, "Failed to invalidate cache for Order {OrderId}", orderId);
        }

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

        await BumpProductListVersionAsync();
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
}
