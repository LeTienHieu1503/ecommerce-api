# Implement CartService — Ecommerce.API

> **Mục tiêu:** Thêm tính năng Giỏ hàng (Cart) theo flow Sequence Diagram, sử dụng Redis làm storage, tích hợp với hệ thống Order/Checkout hiện có.

---

## Thiết kế quyết định (Design Decisions)


| Vấn đề                       | Quyết định                        | Lý do                                                     |
| ---------------------------- | --------------------------------- | --------------------------------------------------------- |
| Cart lưu ở đâu?              | **Redis** (TTL 7 ngày)            | Redis đã có sẵn; cart là dữ liệu tạm thời, không cần ACID |
| Cart có lock stock không?    | **Không** — chỉ validate số lượng | Stock chỉ bị trừ khi tạo Order trong transaction          |
| Cart validate stock lúc nào? | **Add to cart** + **Checkout**    | Double-check để tránh race condition                      |
| User ẩn danh?                | **Không hỗ trợ** — yêu cầu JWT    | Scope hiện tại chỉ có authenticated user                  |
| Cart TTL                     | **7 ngày** sau lần thao tác cuối  | Reset TTL mỗi khi có thay đổi                             |
| Key Redis                    | `cart:{userId}`                   | Mỗi user có 1 cart duy nhất                               |


---

## Cấu trúc file cần tạo/sửa

```
Ecommerce.API/
├── Domain/
│   └── Entities/
│       └── CartItem.cs                          ← MỚI (domain entity, không map DB)
├── Application/
│   ├── DTOs/
│   │   └── Cart/
│   │       ├── CartDto.cs                       ← MỚI
│   │       ├── AddToCartRequest.cs              ← MỚI
│   │       └── UpdateCartItemRequest.cs         ← MỚI
│   ├── Interfaces/
│   │   └── ICartService.cs                      ← MỚI
│   └── Services/
│       └── CartService.cs                       ← MỚI
├── API/
│   └── Controllers/
│       └── CartController.cs                    ← MỚI
└── (OrderService.cs)                            ← SỬA: checkout lấy items từ cart
```

---

## Bước 1 — Domain Entity

**File:** `Domain/Entities/CartItem.cs`

**Logic:** CartItem là value object thuần túy, không có Id, không map DB.

```csharp
namespace Ecommerce.Domain.Entities;

public class CartItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice => UnitPrice * Quantity;
}
```

**File:** `Domain/Entities/Cart.cs`

```csharp
namespace Ecommerce.Domain.Entities;

public class Cart
{
    public Guid UserId { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public decimal TotalAmount => Items.Sum(i => i.TotalPrice);
    public DateTime LastUpdatedAt { get; set; }
}
```

> ⚠️ **Lưu ý:** Không thêm `[Table]` attribute — Cart chỉ tồn tại trong Redis.

---

## Bước 2 — DTOs

**File:** `Application/DTOs/Cart/AddToCartRequest.cs`

```csharp
namespace Ecommerce.Application.DTOs.Cart;

public class AddToCartRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Required]
    [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100")]
    public int Quantity { get; set; }
}
```

**File:** `Application/DTOs/Cart/UpdateCartItemRequest.cs`

```csharp
namespace Ecommerce.Application.DTOs.Cart;

public class UpdateCartItemRequest
{
    [Required]
    [Range(0, 100, ErrorMessage = "Quantity must be between 0 and 100")]
    public int Quantity { get; set; }
    // Quantity = 0 → xóa item khỏi cart
}
```

**File:** `Application/DTOs/Cart/CartDto.cs`

```csharp
namespace Ecommerce.Application.DTOs.Cart;

public class CartDto
{
    public Guid UserId { get; set; }
    public List<CartItemDto> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}

public class CartItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
}
```

---

## Bước 3 — Interface

**File:** `Application/Interfaces/ICartService.cs`

```csharp
namespace Ecommerce.Application.Interfaces;

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid userId);
    Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request);
    Task<CartDto> UpdateCartItemAsync(Guid userId, Guid productId, UpdateCartItemRequest request);
    Task RemoveCartItemAsync(Guid userId, Guid productId);
    Task ClearCartAsync(Guid userId);
}
```

---

## Bước 4 — CartService (Logic chính)

**File:** `Application/Services/CartService.cs`

### Logic chi tiết từng method:

#### 4.1 GetCartAsync

```
1. Tạo Redis key = "cart:{userId}"
2. Lấy data từ Redis (GetStringAsync)
3. Nếu null → trả về CartDto rỗng
4. Deserialize JSON → Cart entity
5. Map sang CartDto → return
```

#### 4.2 AddToCartAsync

```
1. Lấy cart hiện tại từ Redis (hoặc tạo mới nếu chưa có)
2. Gọi IProductRepository để lấy Product theo ProductId
   - Nếu không tìm thấy → throw NotFoundException
   - Nếu Product.Stock < request.Quantity → throw BadRequestException("Insufficient stock")
3. Kiểm tra item đã có trong cart chưa (theo ProductId)
   - Nếu đã có → cộng thêm quantity
     - Validate lại: tổng quantity mới <= Product.Stock
   - Nếu chưa có → tạo CartItem mới
4. Cập nhật Cart.LastUpdatedAt = DateTime.UtcNow
5. Serialize → lưu vào Redis với TTL = 7 ngày (SetStringAsync với TimeSpan)
6. Map → return CartDto
```

#### 4.3 UpdateCartItemAsync

```
1. Lấy cart từ Redis
2. Tìm item theo ProductId → nếu không có → throw NotFoundException
3. Nếu request.Quantity == 0 → xóa item (gọi RemoveCartItemAsync)
4. Nếu request.Quantity > 0:
   - Lấy Product từ DB để validate stock
   - Nếu Product.Stock < request.Quantity → throw BadRequestException
   - Cập nhật item.Quantity
5. Lưu lại Redis với reset TTL
6. Return CartDto
```

#### 4.4 RemoveCartItemAsync

```
1. Lấy cart từ Redis
2. Xóa item khỏi Items list
3. Lưu lại Redis
```

#### 4.5 ClearCartAsync

```
1. Xóa key Redis "cart:{userId}" (KeyDeleteAsync hoặc SetStringAsync(""))
```

### Code CartService:

```csharp
namespace Ecommerce.Application.Services;

public class CartService : ICartService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<CartService> _logger;
    private const int CartTtlDays = 7;

    public CartService(
        IConnectionMultiplexer redis,
        IProductRepository productRepository,
        ILogger<CartService> logger)
    {
        _redis = redis;
        _productRepository = productRepository;
        _logger = logger;
    }

    private string GetCartKey(Guid userId) => $"cart:{userId}";

    private async Task<Cart> GetOrCreateCartAsync(Guid userId)
    {
        var db = _redis.GetDatabase();
        var key = GetCartKey(userId);
        var data = await db.StringGetAsync(key);

        if (data.IsNullOrEmpty)
            return new Cart { UserId = userId };

        return JsonSerializer.Deserialize<Cart>(data!)
               ?? new Cart { UserId = userId };
    }

    private async Task SaveCartAsync(Cart cart)
    {
        var db = _redis.GetDatabase();
        var key = GetCartKey(cart.UserId);
        cart.LastUpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(cart);
        await db.StringSetAsync(key, json, TimeSpan.FromDays(CartTtlDays));
    }

    public async Task<CartDto> GetCartAsync(Guid userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        return MapToDto(cart);
    }

    public async Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request)
    {
        // Bước 1: Lấy product từ DB để validate
        var product = await _productRepository.GetByIdAsync(request.ProductId)
            ?? throw new NotFoundException($"Product {request.ProductId} not found");

        // Bước 2: Lấy cart hiện tại
        var cart = await GetOrCreateCartAsync(userId);

        // Bước 3: Kiểm tra item đã có chưa
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);

        if (existingItem != null)
        {
            var newQty = existingItem.Quantity + request.Quantity;
            if (product.Stock < newQty)
                throw new BadRequestException($"Only {product.Stock} items available in stock");

            existingItem.Quantity = newQty;
        }
        else
        {
            if (product.Stock < request.Quantity)
                throw new BadRequestException($"Only {product.Stock} items available in stock");

            cart.Items.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = request.Quantity
            });
        }

        // Bước 4: Lưu cart
        await SaveCartAsync(cart);

        _logger.LogInformation("User {UserId} added product {ProductId} to cart", userId, request.ProductId);
        return MapToDto(cart);
    }

    public async Task<CartDto> UpdateCartItemAsync(Guid userId, Guid productId, UpdateCartItemRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new NotFoundException($"Product {productId} not found in cart");

        if (request.Quantity == 0)
        {
            cart.Items.Remove(item);
        }
        else
        {
            var product = await _productRepository.GetByIdAsync(productId)
                ?? throw new NotFoundException($"Product {productId} not found");

            if (product.Stock < request.Quantity)
                throw new BadRequestException($"Only {product.Stock} items available in stock");

            item.Quantity = request.Quantity;
            item.UnitPrice = product.Price; // Refresh giá mới nhất
        }

        await SaveCartAsync(cart);
        return MapToDto(cart);
    }

    public async Task RemoveCartItemAsync(Guid userId, Guid productId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            cart.Items.Remove(item);
            await SaveCartAsync(cart);
        }
    }

    public async Task ClearCartAsync(Guid userId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(GetCartKey(userId));
    }

    private static CartDto MapToDto(Cart cart) => new()
    {
        UserId = cart.UserId,
        LastUpdatedAt = cart.LastUpdatedAt,
        TotalAmount = cart.TotalAmount,
        Items = cart.Items.Select(i => new CartItemDto
        {
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            TotalPrice = i.TotalPrice
        }).ToList()
    };
}
```

---

## Bước 5 — CartController

**File:** `API/Controllers/CartController.cs`

```csharp
[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : BaseApiController
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Lấy giỏ hàng của user hiện tại</summary>
    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var cart = await _cartService.GetCartAsync(GetUserId());
        return Ok(ApiResponse<CartDto>.Success(cart));
    }

    /// <summary>Thêm sản phẩm vào giỏ hàng</summary>
    [HttpPost("items")]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
    {
        var cart = await _cartService.AddToCartAsync(GetUserId(), request);
        return Ok(ApiResponse<CartDto>.Success(cart));
    }

    /// <summary>Cập nhật số lượng (quantity=0 sẽ xóa item)</summary>
    [HttpPut("items/{productId:guid}")]
    public async Task<IActionResult> UpdateCartItem(
        Guid productId,
        [FromBody] UpdateCartItemRequest request)
    {
        var cart = await _cartService.UpdateCartItemAsync(GetUserId(), productId, request);
        return Ok(ApiResponse<CartDto>.Success(cart));
    }

    /// <summary>Xóa một sản phẩm khỏi giỏ hàng</summary>
    [HttpDelete("items/{productId:guid}")]
    public async Task<IActionResult> RemoveCartItem(Guid productId)
    {
        await _cartService.RemoveCartItemAsync(GetUserId(), productId);
        return Ok(ApiResponse<string>.Success("Item removed from cart"));
    }

    /// <summary>Xóa toàn bộ giỏ hàng</summary>
    [HttpDelete]
    public async Task<IActionResult> ClearCart()
    {
        await _cartService.ClearCartAsync(GetUserId());
        return Ok(ApiResponse<string>.Success("Cart cleared"));
    }
}
```

---

## Bước 6 — Tích hợp Checkout với Cart

**File:** `Application/Services/OrderService.cs` — sửa method `CheckoutAsync` (hoặc `CreateOrderAsync`)

### Logic hiện tại (không có cart):

```
User POST /api/order với body chứa danh sách items
→ Tạo Order trực tiếp
```

### Logic mới (có cart):

```
User POST /api/order/checkout (không cần body, lấy từ cart)
→ Lấy cart từ Redis theo userId
→ Validate cart không rỗng
→ Validate lại stock từng item (double-check)
→ Tạo Order trong transaction (logic giữ nguyên)
→ Gọi ClearCartAsync sau khi Order tạo thành công
→ Return Order
```

### Thêm vào OrderService:

```csharp
public async Task<OrderDto> CheckoutFromCartAsync(Guid userId)
{
    // Bước 1: Lấy cart
    var cart = await _cartService.GetCartAsync(userId);

    if (!cart.Items.Any())
        throw new BadRequestException("Cart is empty");

    // Bước 2: Map cart items sang CreateOrderRequest
    var createRequest = new CreateOrderRequest
    {
        Items = cart.Items.Select(i => new OrderItemRequest
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity
        }).ToList()
    };

    // Bước 3: Gọi logic tạo Order hiện có (giữ nguyên transaction + stock deduction)
    var order = await CreateOrderAsync(userId, createRequest);

    // Bước 4: Xóa cart sau khi tạo Order thành công
    await _cartService.ClearCartAsync(userId);

    return order;
}
```

### Thêm endpoint vào OrderController:

```csharp
/// <summary>Checkout từ giỏ hàng (không cần truyền items)</summary>
[HttpPost("checkout-from-cart")]
[Authorize]
public async Task<IActionResult> CheckoutFromCart()
{
    var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var order = await _orderService.CheckoutFromCartAsync(userId);
    return Ok(ApiResponse<OrderDto>.Success(order));
}
```

---

## Bước 7 — Đăng ký DI

**File:** `API/Extensions/ApplicationServiceExtensions.cs` (hoặc `AddApplicationServices`)

```csharp
services.AddScoped<ICartService, CartService>();
```

> ⚠️ Redis `IConnectionMultiplexer` phải đã được đăng ký — kiểm tra trong `AddInfrastructure`.

---

## Endpoints tổng hợp


| Method | Endpoint                        | Mô tả                | Auth |
| ------ | ------------------------------- | -------------------- | ---- |
| GET    | `/api/cart`                     | Lấy giỏ hàng         | User |
| POST   | `/api/cart/items`               | Thêm sản phẩm        | User |
| PUT    | `/api/cart/items/{productId}`   | Cập nhật số lượng    | User |
| DELETE | `/api/cart/items/{productId}`   | Xóa 1 item           | User |
| DELETE | `/api/cart`                     | Xóa toàn bộ giỏ hàng | User |
| POST   | `/api/order/checkout-from-cart` | Tạo đơn từ cart      | User |


---

## Rủi ro và lưu ý (Caveats)


| Rủi ro             | Mô tả                                       | Cách xử lý                                                           |
| ------------------ | ------------------------------------------- | -------------------------------------------------------------------- |
| **Giá thay đổi**   | Giá sản phẩm có thể đổi sau khi add to cart | `UpdateCartItemAsync` refresh giá; hiển thị warning nếu giá thay đổi |
| **Hết hàng**       | Stock thay đổi giữa add và checkout         | Double-check trong `CheckoutFromCartAsync`                           |
| **Redis down**     | Cart không lấy được                         | Cần fallback hoặc throw lỗi rõ ràng (không silent fail)              |
| **Cart stale**     | TTL hết nhưng user quay lại                 | Trả về cart rỗng, không lỗi                                          |
| **Race condition** | 2 tab cùng checkout 1 cart                  | Optimistic concurrency trong OrderService đã xử lý (xmin)            |


---

## Thứ tự implement

1. `CartItem.cs` + `Cart.cs`
2. DTOs (`AddToCartRequest`, `UpdateCartItemRequest`, `CartDto`)
3. `ICartService.cs`
4. `CartService.cs`
5. `CartController.cs`
6. Đăng ký DI
7. Sửa `OrderService` + `OrderController` (checkout from cart)
8. Test thủ công qua Swagger
9. Viết Unit Test cho `CartService`

