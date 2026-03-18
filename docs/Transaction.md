# 🤖 AI EXECUTION PLAN — ORDER SYSTEM (TRANSACTION + CONCURRENCY + REDIS + LOGGING)

---

# 🎯 GOAL

Implement Order system with:

* Transaction safety
* Concurrency control (RowVersion)
* Redis cache invalidation
* Structured logging
* Production-ready behavior

---

# ⚙️ GLOBAL RULES

* DO NOT modify existing working features
* ALL changes must be backward compatible
* USE single SaveChanges per transaction
* DB is source of truth
* Redis is cache only
* ID Types: Use `int` for all Entity IDs (consistent with Project)

---

# 📦 STEP 1 — DOMAIN UPDATE

## Add OrderStatus Enum

**File:** `Ecommerce.Domain/Common/Enums/OrderStatus.cs`

```csharp
namespace Ecommerce.Domain.Common.Enums;

public enum OrderStatus
{
    Pending = 1,
    Paid = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 0
}
```

---

## Add Order Entity

**File:** `Ecommerce.Domain/Entities/Order.cs`

```csharp
using Ecommerce.Domain.Common.Enums;

namespace Ecommerce.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public byte[] RowVersion { get; set; } = null!;

    public List<OrderItem> Items { get; set; } = new();
}
```

---

## Add OrderItem Entity

**File:** `Ecommerce.Domain/Entities/OrderItem.cs`

```csharp
namespace Ecommerce.Domain.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
```

---

## Update Product Entity

**File:** `Ecommerce.Domain/Entities/Product.cs`

```csharp
public int Stock { get; set; }
public byte[] RowVersion { get; set; } = null!;
```

---

# 🧱 STEP 2 — EF CONFIGURATION

## ProductConfiguration

**File:** `Ecommerce.Infrastructure/Configurations/ProductConfiguration.cs` (Create if not exists)

```csharp
builder.Property(p => p.RowVersion)
    .IsRowVersion();

builder.Property(p => p.Price)
    .HasPrecision(18, 2);
```

**File:** `Ecommerce.Infrastructure/Configurations/OrderConfiguration.cs`

```csharp
builder.Property(o => o.RowVersion)
    .IsRowVersion();
```

---

## DbContext

**File:** `Ecommerce.Infrastructure/Data/ApplicationDbContext.cs`

```csharp
public DbSet<Order> Orders { get; set; } = null!;
public DbSet<OrderItem> OrderItems { get; set; } = null!;
```

---

## Migration

```bash
Add-Migration AddOrderSystem
Update-Database
```

---

# 🗃️ STEP 3 — REPOSITORY

## Interface

**File:** `Ecommerce.Domain/Interfaces/IOrderRepository.cs`

```csharp
using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Interfaces;

public interface IOrderRepository
{
    Task AddAsync(Order order);
    Task<Order?> GetByIdAsync(int id);
    Task<IEnumerable<Order>> GetByUserIdAsync(int userId);
    Task UpdateAsync(Order order);
}
```

---

## Implementation

**File:** `Ecommerce.Infrastructure/Repositories/OrderRepository.cs`

```csharp
using Ecommerce.Infrastructure.Data;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;

    public OrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Order order)
    {
        await _context.Orders.AddAsync(order);
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<IEnumerable<Order>> GetByUserIdAsync(int userId)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .ToListAsync();
    }

    public async Task UpdateAsync(Order order)
    {
        _context.Orders.Update(order);
        await Task.CompletedTask; // EF tracks changes, SaveChangesAsync will be called in service
    }
}
```

---

# 🧠 STEP 4 — ORDER SERVICE

## DTOs

**File:** `Ecommerce.Application/DTOs/Order/CreateOrderRequest.cs`

```csharp
public class CreateOrderRequest
{
    public int UserId { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
```

**File:** `Ecommerce.Application/DTOs/Order/OrderDto.cs`

```csharp
public class OrderDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
}
```

## Service Implementation

**File:** `Ecommerce.Application/Services/OrderService.cs`

```csharp
public async Task CreateOrderAsync(CreateOrderRequest request)
{
    var correlationId = Guid.NewGuid().ToString();
    _logger.LogInformation("CreateOrder started | CorrelationId={cid}", correlationId);

    using var transaction = await _dbContext.Database.BeginTransactionAsync();

    try
    {
        var order = new Order
        {
            UserId = request.UserId,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            Items = new List<OrderItem>()
        };

        foreach (var itemRequest in request.Items)
        {
            var product = await _productRepo.GetByIdAsync(itemRequest.ProductId);
            if (product == null) throw new BusinessException($"Product {itemRequest.ProductId} not found");

            if (product.Stock < itemRequest.Quantity)
                throw new BusinessException($"Out of stock for Product {product.Name}");

            product.Stock -= itemRequest.Quantity;
            order.Items.Add(new OrderItem
            {
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                Price = product.Price
            });
        }

        await _orderRepo.AddAsync(order);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        foreach (var item in order.Items)
            await _cache.RemoveAsync($"product:{item.ProductId}");

        _logger.LogInformation("Order created successfully | OrderId={oid}", order.Id);
    }
    catch (DbUpdateConcurrencyException)
    {
        await transaction.RollbackAsync();
        _logger.LogError("Concurrency conflict detected during creation");
        throw new BusinessException("Inventory updated by another process, please retry.");
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        _logger.LogError(ex, "Transaction failed");
        throw;
    }
}

public async Task<OrderDto?> GetOrderByIdAsync(int id)
{
    var order = await _orderRepo.GetByIdAsync(id);
    return order != null ? MapToDto(order) : null;
}

public async Task CancelOrderAsync(int orderId)
{
    using var transaction = await _dbContext.Database.BeginTransactionAsync();
    try
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) throw new BusinessException("Order not found");
        
        if (order.Status != OrderStatus.Pending) 
            throw new BusinessException("Only pending orders can be cancelled");

        order.Status = OrderStatus.Cancelled;
        
        foreach(var item in order.Items)
        {
            var product = await _productRepo.GetByIdAsync(item.ProductId);
            if (product != null) 
            {
                product.Stock += item.Quantity;
                await _cache.RemoveAsync($"product:{product.Id}");
            }
        }

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        _logger.LogInformation("Order cancelled | OrderId={oid}", orderId);
    }
    catch (DbUpdateConcurrencyException)
    {
        await transaction.RollbackAsync();
        throw new BusinessException("Order or Product was updated by another process, retry");
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        _logger.LogError(ex, "CancelOrder failed");
        throw;
    }
}
```

---

# 🔄 STEP 4.1 — CANCELLATION FLOW

1. **User/Admin** calls `CancelOrder`.
2. **Check Status**: Only `Pending` orders can be cancelled.
3. **Restore Stock**: Iterate through `OrderItems` and add quantity back to `Product.Stock`.
4. **Update Status**: Set `Order.Status = OrderStatus.Cancelled`.
5. **Transaction**: Wrap in transaction if multiple DB calls are involved (already handled by `SaveChangesAsync` in this simple case, but good to note).

---

# 🪵 STEP 5 — LOGGING

## Required fields

* CorrelationId
* UserId
* ProductId
* OrderId
* Duration

---

## Example Logs

```
[INFO] CreateOrder started | CorrelationId=abc
[WARNING] Out of stock | ProductId=1
[ERROR] Concurrency conflict
[INFO] Cache invalidated | product:1
[INFO] Order created successfully | OrderId=123
```

---

# 🧠 STEP 6 — REDIS CACHE

## Rules

* NEVER read stock from cache
* ALWAYS read DB inside transaction

---

## Invalidate after commit

```csharp
await _cache.RemoveAsync($"product:{product.Id}");
```

---

## Logging

```
[INFO] Cache hit
[INFO] Cache miss
[INFO] Cache invalidated
```

---

# 🌐 STEP 7 — CONTROLLER

**File:** `Ecommerce.API/Controllers/OrderController.cs`

```csharp
using Ecommerce.API.Responses;

[HttpPost]
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    await _orderService.CreateOrderAsync(request);
    return Ok(new ApiResponse<string>(200, true, "Order created", null));
}

[HttpPut("{id}/cancel")]
public async Task<IActionResult> CancelOrder(int id)
{
    await _orderService.CancelOrderAsync(id);
    return Ok(new ApiResponse<string>(200, true, "Order cancelled", null));
}
```
```

---

# 🧪 STEP 8 — UNIT TEST

**File:** `Ecommerce.UnitTests/Services/OrderServiceTests.cs`

## Cases

* CreateOrder_Success
* CreateOrder_OutOfStock
* CreateOrder_ConcurrencyException
* CreateOrder_Rollback
* CancelOrder_Success
* CancelOrder_AlreadyProcessed (Cannot cancel if Shipped/Delivered)

---

# 💣 STEP 9 — FAILURE HANDLING

| Scenario    | Behavior                |
| ----------- | ----------------------- |
| DB fail     | Rollback                |
| Redis fail  | Log only                |
| Concurrency | Throw BusinessException |

---

# 🔍 STEP 10 — VALIDATION

* Run 2 request cùng lúc
* Stock không âm
* Order đúng số lượng

---

# 📊 STEP 11 — LOG FILE

**Path:** `/logs/order-log.txt`

---

# 🚫 ANTI-PATTERN

* Multiple SaveChanges ❌
* No transaction ❌
* Cache for stock ❌
* No concurrency handling ❌
* No logging ❌

---

# ✅ FINAL CHECKLIST

* [ ] Order + OrderItem created
* [ ] RowVersion configured
* [ ] Transaction implemented
* [ ] Concurrency handled
* [ ] Redis invalidated
* [ ] Logging working
* [ ] Unit test covered
* [ ] **Cancellation Flow with Stock Restoration** ✅

---

# 🚀 END
