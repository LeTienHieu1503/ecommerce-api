using Ecommerce.Application.DTOs.Order;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Common.Enums;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ecommerce.Domain.Common.Pagination;

public class OrderServiceTests
{
    // =============================================
    // Dependencies mock
    // =============================================
    private readonly Mock<IOrderRepository> _orderRepo = new();
    private readonly Mock<IProductRepository> _productRepo = new();
    private readonly Mock<ICartService> _cartService = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IPaymentService> _paymentService = new();
    private readonly Mock<ILogger<OrderService>> _logger = new();
    private readonly Mock<IRequestDeviceContext> _requestDeviceContext = new();
    private readonly Mock<IUnitOfWorkTransaction> _transaction = new();

    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        // Setup transaction mock — BeginTransactionAsync trả về transaction giả
        _unitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(_transaction.Object);
        _transaction.Setup(t => t.CommitAsync())
            .Returns(Task.CompletedTask);
        _transaction.Setup(t => t.RollbackAsync())
            .Returns(Task.CompletedTask);
        _transaction.Setup(t => t.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _paymentService
            .Setup(p => p.GetReusablePaymentIntentAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync((PaymentIntentCreateResult?)null);

        _sut = new OrderService(
            _orderRepo.Object,
            _productRepo.Object,
            _cartService.Object,
            _unitOfWork.Object,
            _cache.Object,
            _paymentService.Object,
            _logger.Object,
            _requestDeviceContext.Object);
    }

    // =============================================
    // Helper
    // =============================================
    private static Product CreateFakeProduct(
        int id = 1,
        string name = "Product A",
        int stock = 10,
        decimal price = 100m)
        => new()
        {
            Id = id,
            Name = name,
            Stock = stock,
            Price = price
        };

    private static Order CreateFakeOrder(
        int id = 1,
        int userId = 1,
        OrderStatus status = OrderStatus.Pending,
        DateTime? createdAt = null,
        PaymentStatus paymentStatus = PaymentStatus.Pending,
        string? stripePaymentIntentId = null)
        => new()
        {
            Id = id,
            UserId = userId,
            Status = status,
            PaymentStatus = paymentStatus,
            StripePaymentIntentId = stripePaymentIntentId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Items = new List<OrderItem>
            {
                new() { Id = id * 10 + 1, OrderId = id, ProductId = 1, Quantity = 2, Price = 100m }
            }
        };

    private static CreateOrderRequest CreateFakeRequest(
        int userId = 1,
        int productId = 1,
        int quantity = 2)
        => new()
        {
            UserId = userId,
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = productId, Quantity = quantity }
            }
        };

    // =============================================
    // CREATEORDER TESTS
    // =============================================

    [Fact]
    public async Task CreateOrderAsync_WhenItemsEmpty_ThrowsBusinessException()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            UserId = 1,
            Items = new List<OrderItemRequest>() // ← rỗng
        };

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*at least one item*");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenItemsNull_ThrowsBusinessException()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            UserId = 1,
            Items = null! // ← null
        };

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*at least one item*");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenQuantityZero_ThrowsBusinessException()
    {
        // Arrange
        var request = CreateFakeRequest(quantity: 0); // ← quantity = 0

        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(CreateFakeProduct());

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*greater than 0*");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenProductNotFound_ThrowsBusinessException()
    {
        // Arrange
        var request = CreateFakeRequest();

        // Product không tồn tại
        _productRepo.Setup(p => p.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product?)null);

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenOutOfStock_ThrowsBusinessException()
    {
        // Arrange — stock chỉ có 1 nhưng order 5
        var request = CreateFakeRequest(quantity: 5);
        var product = CreateFakeProduct(stock: 1); // ← không đủ

        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*cannot exceed available stock*Maximum: 1*requested: 5*");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenGroupedQuantityExceedsStock_ThrowsBusinessException()
    {
        var request = new CreateOrderRequest
        {
            UserId = 1,
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 4 },
                new() { ProductId = 1, Quantity = 4 }
            }
        };
        var product = CreateFakeProduct(stock: 7);

        _productRepo.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(product);

        var act = () => _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*Maximum: 7*requested: 8*");
    }

    [Fact]
    public async Task CreateOrderAsync_WhenValidRequest_DeductsStockAndCreatesOrder()
    {
        // Arrange
        var request = CreateFakeRequest(quantity: 2);
        var product = CreateFakeProduct(stock: 10); // ← đủ stock

        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);
        _orderRepo.Setup(o => o.AddAsync(It.IsAny<Order>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _sut.CreateOrderAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(1);
        result.Status.Should().Be("Pending");

        // Stock phải bị trừ đi 2
        product.Stock.Should().Be(8);

        // Transaction phải được commit
        _transaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenDuplicateProductIds_GroupsAndDeductsOnce()
    {
        // Arrange — cùng 1 product nhưng gửi 2 lần
        var request = new CreateOrderRequest
        {
            UserId = 1,
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 2 },
                new() { ProductId = 1, Quantity = 3 } // ← cùng product
            }
        };

        var product = CreateFakeProduct(stock: 10);

        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);
        _orderRepo.Setup(o => o.AddAsync(It.IsAny<Order>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _sut.CreateOrderAsync(request);

        // Assert — stock bị trừ đúng tổng quantity (2+3=5)
        product.Stock.Should().Be(5);

        // GetByIdAsync chỉ gọi 1 lần dù có 2 item cùng product
        _productRepo.Verify(p => p.GetByIdAsync(1), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenConcurrencyConflict_RollsBackAndThrows()
    {
        // Arrange
        var request = CreateFakeRequest();
        var product = CreateFakeProduct(stock: 10);

        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);
        _orderRepo.Setup(o => o.AddAsync(It.IsAny<Order>()))
            .Returns(Task.CompletedTask);

        // SaveChanges throw DbUpdateConcurrencyException
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*another process*");

        // Transaction phải bị rollback
        _transaction.Verify(t => t.RollbackAsync(), Times.Once);
        _transaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenCacheFails_StillCreatesOrder()
    {
        // Arrange — cache lỗi nhưng order vẫn phải tạo được
        var request = CreateFakeRequest();
        var product = CreateFakeProduct(stock: 10);

        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);
        _orderRepo.Setup(o => o.AddAsync(It.IsAny<Order>()))
            .Returns(Task.CompletedTask);

        // Cache throw exception
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Redis down"));
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new Exception("Redis down"));

        // Act
        var act = () => _sut.CreateOrderAsync(request);

        // Assert — cache lỗi nhưng KHÔNG throw
        await act.Should().NotThrowAsync();
    }

    // =============================================
    // GETORDERBYID TESTS
    // =============================================

    [Fact]
    public async Task GetOrderByIdAsync_WhenOrderExists_ReturnsOrderDto()
    {
        // Arrange
        var order = CreateFakeOrder();
        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);

        // Act
        var result = await _sut.GetOrderByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task GetOrderByIdAsync_WhenOrderNotFound_ReturnsNull()
    {
        // Arrange
        _orderRepo.Setup(o => o.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Order?)null);

        // Act
        var result = await _sut.GetOrderByIdAsync(99);

        // Assert
        result.Should().BeNull();
    }

    // =============================================
    // GETALLORDERS (paged / search / sort)
    // =============================================

    [Fact]
    public async Task GetAllOrdersAsync_WithPaging_ReturnsCorrectSliceAndTotal()
    {
        var orders = new List<Order>
        {
            CreateFakeOrder(1, createdAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateFakeOrder(2, createdAt: new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            CreateFakeOrder(3, createdAt: new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc))
        };
        _orderRepo.Setup(o => o.GetQueryable()).Returns(orders.AsQueryable());

        var result = await _sut.GetAllOrdersAsync(new OrderQuery { Page = 1, PageSize = 2 });

        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task GetAllOrdersAsync_SearchNumeric_FiltersByOrderIdOrUserId()
    {
        var orders = new List<Order>
        {
            CreateFakeOrder(1, userId: 10),
            CreateFakeOrder(2, userId: 20),
            CreateFakeOrder(3, userId: 10)
        };
        _orderRepo.Setup(o => o.GetQueryable()).Returns(orders.AsQueryable());

        var byOrderId = await _sut.GetAllOrdersAsync(new OrderQuery { Search = "2", PageSize = 20 });
        byOrderId.TotalCount.Should().Be(1);
        byOrderId.Items.Single().Id.Should().Be(2);

        var byUserId = await _sut.GetAllOrdersAsync(new OrderQuery { Search = "10", PageSize = 20 });
        byUserId.TotalCount.Should().Be(2);
        byUserId.Items.Select(x => x.Id).Should().BeEquivalentTo([1, 3]);
    }

    [Fact]
    public async Task GetAllOrdersAsync_SearchStatusName_FiltersOrders()
    {
        var orders = new List<Order>
        {
            CreateFakeOrder(1, status: OrderStatus.Pending),
            CreateFakeOrder(2, status: OrderStatus.Paid)
        };
        _orderRepo.Setup(o => o.GetQueryable()).Returns(orders.AsQueryable());

        var result = await _sut.GetAllOrdersAsync(new OrderQuery { Search = "Paid", PageSize = 20 });

        result.TotalCount.Should().Be(1);
        result.Items.Single().Status.Should().Be("Paid");
    }

    [Fact]
    public async Task GetAllOrdersAsync_StatusFilter_CombinesWithQuery()
    {
        var orders = new List<Order>
        {
            CreateFakeOrder(1, status: OrderStatus.Pending),
            CreateFakeOrder(2, status: OrderStatus.Paid)
        };
        _orderRepo.Setup(o => o.GetQueryable()).Returns(orders.AsQueryable());

        var result = await _sut.GetAllOrdersAsync(new OrderQuery
        {
            Status = OrderStatus.Pending,
            PageSize = 20
        });

        result.TotalCount.Should().Be(1);
        result.Items.Single().Id.Should().Be(1);
    }

    [Fact]
    public async Task GetAllOrdersAsync_SortByIdAscending_OrdersCorrectly()
    {
        var orders = new List<Order>
        {
            CreateFakeOrder(3, createdAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateFakeOrder(1, createdAt: new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            CreateFakeOrder(2, createdAt: new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc))
        };
        _orderRepo.Setup(o => o.GetQueryable()).Returns(orders.AsQueryable());

        var result = await _sut.GetAllOrdersAsync(new OrderQuery
        {
            SortBy = "id",
            SortOrder = "asc",
            PageSize = 10
        });

        result.Items.Select(x => x.Id).Should().ContainInOrder(1, 2, 3);
    }

    // =============================================
    // GETORDERSBYUSERID TESTS
    // =============================================

    [Fact]
    public async Task GetOrdersByUserIdAsync_WhenOrdersExist_ReturnsAllOrders()
    {
        // Arrange
        var orders = new List<Order>
        {
            CreateFakeOrder(id: 1),
            CreateFakeOrder(id: 2)
        };

        _orderRepo.Setup(o => o.GetByUserIdAsync(1))
            .ReturnsAsync(orders);

        // Act
        var result = await _sut.GetOrdersByUserIdAsync(1);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOrdersByUserIdAsync_WhenNoOrders_ReturnsEmptyList()
    {
        // Arrange
        _orderRepo.Setup(o => o.GetByUserIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Order>());

        // Act
        var result = await _sut.GetOrdersByUserIdAsync(99);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrdersForCurrentUserAsync_WhenUserIdPresent_ReturnsOrdersForThatUser()
    {
        _requestDeviceContext.SetupGet(c => c.UserId).Returns(1);
        var orders = new List<Order> { CreateFakeOrder(id: 1) };
        _orderRepo.Setup(o => o.GetByUserIdAsync(1)).ReturnsAsync(orders);

        var result = await _sut.GetOrdersForCurrentUserAsync();

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOrdersForCurrentUserAsync_WhenUserIdMissing_ThrowsUnauthorizedException()
    {
        _requestDeviceContext.SetupGet(c => c.UserId).Returns((int?)null);

        var act = () => _sut.GetOrdersForCurrentUserAsync();

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("User identifier is not available.");
    }

    // =============================================
    // CANCELORDER TESTS
    // =============================================

    [Fact]
    public async Task CancelOrderAsync_WhenOrderNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _orderRepo.Setup(o => o.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Order?)null);

        // Act
        var act = () => _sut.CancelOrderAsync(99, currentUserId: 1);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Order 99 not found*");
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderNotPending_ThrowsBusinessException()
    {
        // Arrange — order đã Cancelled rồi
        var order = CreateFakeOrder(status: OrderStatus.Cancelled);
        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);

        // Act
        var act = () => _sut.CancelOrderAsync(1, currentUserId: 1);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*Cannot cancel*");
    }

    [Fact]
    public async Task CancelOrderAsync_WhenNotOwner_ThrowsForbiddenException()
    {
        var order = CreateFakeOrder(status: OrderStatus.Pending, userId: 10);
        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);

        var act = () => _sut.CancelOrderAsync(1, currentUserId: 99, canCancelAnyOrder: false);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*not authorized to cancel*");
    }

    [Fact]
    public async Task CancelOrderAsync_WhenAdmin_CanCancelAnotherUsersOrder()
    {
        var product = CreateFakeProduct(stock: 8);
        var order = CreateFakeOrder(status: OrderStatus.Pending, userId: 10);

        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);
        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        await _sut.CancelOrderAsync(1, currentUserId: 99, canCancelAnyOrder: true);

        order.Status.Should().Be(OrderStatus.Cancelled);
        _transaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenValidPendingOrder_CancelsAndRestoresStock()
    {
        // Arrange
        var product = CreateFakeProduct(stock: 8);
        var order = CreateFakeOrder(status: OrderStatus.Pending);

        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);
        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        await _sut.CancelOrderAsync(1, currentUserId: 1);

        // Assert — status phải là Cancelled
        order.Status.Should().Be(OrderStatus.Cancelled);

        // Stock phải được hoàn lại (+2)
        product.Stock.Should().Be(10);

        // Transaction commit
        _transaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenProductNotFoundDuringRestore_LogsWarningAndContinues()
    {
        // Arrange — product bị xóa nhưng cancel order vẫn phải chạy được
        var order = CreateFakeOrder(status: OrderStatus.Pending);

        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);

        // Product không tìm thấy
        _productRepo.Setup(p => p.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product?)null);

        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var act = () => _sut.CancelOrderAsync(1, currentUserId: 1);

        // Assert — không throw, chỉ log warning
        await act.Should().NotThrowAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenConcurrencyConflict_RollsBackAndThrows()
    {
        // Arrange
        var order = CreateFakeOrder(status: OrderStatus.Pending);
        var product = CreateFakeProduct(stock: 8);

        _orderRepo.Setup(o => o.GetByIdAsync(1))
            .ReturnsAsync(order);
        _productRepo.Setup(p => p.GetByIdAsync(1))
            .ReturnsAsync(product);

        // SaveChanges throw concurrency
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var act = () => _sut.CancelOrderAsync(1, currentUserId: 1);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*another process*");

        _transaction.Verify(t => t.RollbackAsync(), Times.Once);
        _transaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenOrderMissing_ThrowsNotFoundException()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Order?)null);

        var act = () => _sut.CreateCheckoutAsync(99, userId: 1);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenWrongUser_ThrowsNotFoundException()
    {
        var order = CreateFakeOrder(id: 1, userId: 5);
        _orderRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);

        var act = () => _sut.CreateCheckoutAsync(1, userId: 1);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenOrderNotPending_ThrowsBusinessException()
    {
        var order = CreateFakeOrder(status: OrderStatus.Paid);
        _orderRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);

        var act = () => _sut.CreateCheckoutAsync(1, userId: 1);

        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*pending orders*");
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenValid_ReturnsClientSecretAndPersistsIntent()
    {
        var order = CreateFakeOrder();
        _orderRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);
        _paymentService
            .Setup(p => p.CreatePaymentIntentAsync(20000, "usd", "1", "order-1-usd20000-v2"))
            .ReturnsAsync(new PaymentIntentCreateResult("secret_val", "pi_abc"));

        var result = await _sut.CreateCheckoutAsync(1, userId: 1);

        result.ClientSecret.Should().Be("secret_val");
        order.StripePaymentIntentId.Should().Be("pi_abc");
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenExistingIntentReusable_ReturnsSecretWithoutCreate()
    {
        var order = CreateFakeOrder(stripePaymentIntentId: "pi_existing");
        _orderRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);
        _paymentService
            .Setup(p => p.GetReusablePaymentIntentAsync("pi_existing", 20000, "usd"))
            .ReturnsAsync(new PaymentIntentCreateResult("reuse_secret", "pi_existing"));

        var result = await _sut.CreateCheckoutAsync(1, userId: 1);

        result.ClientSecret.Should().Be("reuse_secret");
        _paymentService.Verify(
            p => p.CreatePaymentIntentAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenPaymentFailed_UsesRetryIdempotencyKeyPrefix()
    {
        var order = CreateFakeOrder(paymentStatus: PaymentStatus.Failed, stripePaymentIntentId: "pi_old");
        _orderRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);
        string? capturedKey = null;
        _paymentService
            .Setup(p => p.CreatePaymentIntentAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<long, string, string, string>((_, _, _, key) => capturedKey = key)
            .ReturnsAsync(new PaymentIntentCreateResult("secret_new", "pi_new"));

        var result = await _sut.CreateCheckoutAsync(1, userId: 1);

        result.ClientSecret.Should().Be("secret_new");
        order.StripePaymentIntentId.Should().Be("pi_new");
        capturedKey.Should().NotBeNull();
        capturedKey.Should().MatchRegex("^order-1-retry-[a-f0-9]{32}$");
        _paymentService.Verify(
            p => p.GetReusablePaymentIntentAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task HandlePaymentSucceededAsync_WhenOrderFound_UpdatesStatus()
    {
        var order = CreateFakeOrder(paymentStatus: PaymentStatus.Pending, stripePaymentIntentId: "pi_abc");
        _orderRepo.Setup(r => r.GetByStripePaymentIntentIdAsync("pi_abc")).ReturnsAsync(order);

        await _sut.HandlePaymentSucceededAsync("pi_abc");

        order.Status.Should().Be(OrderStatus.Paid);
        order.PaymentStatus.Should().Be(PaymentStatus.Succeeded);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandlePaymentSucceededAsync_WhenAlreadyPaid_IsIdempotent()
    {
        var order = CreateFakeOrder(status: OrderStatus.Paid, paymentStatus: PaymentStatus.Succeeded, stripePaymentIntentId: "pi_abc");
        _orderRepo.Setup(r => r.GetByStripePaymentIntentIdAsync("pi_abc")).ReturnsAsync(order);

        await _sut.HandlePaymentSucceededAsync("pi_abc");

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandlePaymentSucceededAsync_WhenOrderCancelled_DoesNotMarkPaid()
    {
        var order = CreateFakeOrder(
            status: OrderStatus.Cancelled,
            paymentStatus: PaymentStatus.Cancelled,
            stripePaymentIntentId: "pi_abc");
        _orderRepo.Setup(r => r.GetByStripePaymentIntentIdAsync("pi_abc")).ReturnsAsync(order);

        await _sut.HandlePaymentSucceededAsync("pi_abc");

        order.Status.Should().Be(OrderStatus.Cancelled);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandlePaymentFailedAsync_WhenOrderFound_SetsFailed()
    {
        var order = CreateFakeOrder(paymentStatus: PaymentStatus.Pending, stripePaymentIntentId: "pi_abc");
        _orderRepo.Setup(r => r.GetByStripePaymentIntentIdAsync("pi_abc")).ReturnsAsync(order);

        await _sut.HandlePaymentFailedAsync("pi_abc");

        order.PaymentStatus.Should().Be(PaymentStatus.Failed);
        order.Status.Should().Be(OrderStatus.Pending);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandlePaymentFailedAsync_WhenAlreadySucceeded_IsNoOp()
    {
        var order = CreateFakeOrder(status: OrderStatus.Paid, paymentStatus: PaymentStatus.Succeeded, stripePaymentIntentId: "pi_abc");
        _orderRepo.Setup(r => r.GetByStripePaymentIntentIdAsync("pi_abc")).ReturnsAsync(order);

        await _sut.HandlePaymentFailedAsync("pi_abc");

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandlePaymentSucceededAsync_WhenRefunded_DoesNotMarkPaid()
    {
        var order = CreateFakeOrder(
            status: OrderStatus.Cancelled,
            paymentStatus: PaymentStatus.Refunded,
            stripePaymentIntentId: "pi_abc");
        _orderRepo.Setup(r => r.GetByStripePaymentIntentIdAsync("pi_abc")).ReturnsAsync(order);

        await _sut.HandlePaymentSucceededAsync("pi_abc");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefundPaidOrderAsync_WhenOrderNotFound_ThrowsNotFoundException()
    {
        _orderRepo.Setup(o => o.GetByIdAsync(99)).ReturnsAsync((Order?)null);

        var act = () => _sut.RefundPaidOrderAsync(99);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("*Order 99 not found*");
        _paymentService.Verify(
            p => p.CreateRefundForPaymentIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefundPaidOrderAsync_WhenAlreadyRefunded_DoesNotCallStripe()
    {
        var order = CreateFakeOrder(
            status: OrderStatus.Cancelled,
            paymentStatus: PaymentStatus.Refunded,
            stripePaymentIntentId: "pi_x");
        _orderRepo.Setup(o => o.GetByIdAsync(1)).ReturnsAsync(order);

        var dto = await _sut.RefundPaidOrderAsync(1);

        dto.PaymentStatus.Should().Be(PaymentStatus.Refunded.ToString());
        _paymentService.Verify(
            p => p.CreateRefundForPaymentIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefundPaidOrderAsync_WhenShipped_ThrowsBusinessException()
    {
        var order = CreateFakeOrder(
            status: OrderStatus.Shipped,
            paymentStatus: PaymentStatus.Succeeded,
            stripePaymentIntentId: "pi_x");
        _orderRepo.Setup(o => o.GetByIdAsync(1)).ReturnsAsync(order);

        var act = () => _sut.RefundPaidOrderAsync(1);

        await act.Should().ThrowAsync<BusinessException>().WithMessage("*Only paid orders*");
        _paymentService.Verify(
            p => p.CreateRefundForPaymentIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefundPaidOrderAsync_WhenValid_RefundsRestoresStockAndSetsRefundId()
    {
        var product = CreateFakeProduct(stock: 8);
        var order = CreateFakeOrder(
            status: OrderStatus.Paid,
            paymentStatus: PaymentStatus.Succeeded,
            stripePaymentIntentId: "pi_pay");
        _orderRepo.Setup(o => o.GetByIdAsync(1)).ReturnsAsync(order);
        _productRepo.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(product);
        _paymentService
            .Setup(p => p.CreateRefundForPaymentIntentAsync("pi_pay", "refund-order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundCreateResult("re_new"));
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>())).ReturnsAsync(1L);

        var dto = await _sut.RefundPaidOrderAsync(1);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        order.StripeRefundId.Should().Be("re_new");
        product.Stock.Should().Be(10);
        dto.StripeRefundId.Should().Be("re_new");
        _transaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RefundPaidOrderAsync_WhenStripeFails_DoesNotOpenTransaction()
    {
        var order = CreateFakeOrder(
            status: OrderStatus.Paid,
            paymentStatus: PaymentStatus.Succeeded,
            stripePaymentIntentId: "pi_pay");
        _orderRepo.Setup(o => o.GetByIdAsync(1)).ReturnsAsync(order);
        _paymentService
            .Setup(p => p.CreateRefundForPaymentIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stripe down"));

        var act = () => _sut.RefundPaidOrderAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stripe down*");
        _unitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }
}