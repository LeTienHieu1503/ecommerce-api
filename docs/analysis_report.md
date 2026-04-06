# Báo cáo Phân tích Dự án Ecommerce.API

Cập nhật theo mã nguồn hiện tại (04/2026): controllers, services, test, cấu hình solution `Ecommerce.API.sln`.

## 1. Những gì đã làm được (Completed)

Dự án theo **Clean Architecture** (Domain / Application / Infrastructure / API), có **Stripe** cho thanh toán và **Redis** cho cache, blacklist token, và **giỏ hàng (cart)**.

### Kiến trúc & pattern

- **Repository + Unit of Work**: transaction `BeginTransactionAsync()` cho luồng cần ACID (tạo đơn, hủy đơn, refund, …).
- **Mapper tĩnh** (không AutoMapper).
- **BaseApiController + ApiResponse**: format response thống nhất.
- **Program.cs**: cấu hình qua `Extensions` (`AddInfrastructure`, `AddApplicationServices`, `AddAuthConfig`, `AddSwaggerConfig`); **IRequestDeviceContext** + **HttpRequestDeviceContext** + `AddHttpContextAccessor` cho ngữ cảnh request/thiết bị và **UserId** (int?) lấy từ claim `NameIdentifier` sau khi JWT hợp lệ — dùng trong service layer (ví dụ `GetOrdersForCurrentUserAsync`).

### API (Controllers)

- **Auth** (`api/auth`): đăng ký, đăng nhập, refresh, đăng xuất, **GET me** (id, email, roles, device bound) — chưa profile CRUD / đổi mật khẩu / quên mật khẩu.
- **Category / Product**: CRUD, phân trang, sort, tìm kiếm.
- **Cart** (`api/cart`): **JWT bắt buộc** (`[Authorize]`). Lấy giỏ, thêm/cập nhật/xóa dòng, xóa cả giỏ. Lưu **Redis** key `cart:{userId}`, TTL 7 ngày, JSON camelCase; validate tồn kho khi thêm/sửa; domain `Cart` / `CartItem` không map DB.
- **Order**: không còn endpoint **POST** tạo đơn bằng body items trực tiếp; luồng mua: giỏ → `**POST /api/Order/add-from-cart`** (policy `order.create`) → `CreateOrderAsync` + trừ kho + xóa giỏ (best-effort clear Redis). Chi tiết đơn, **cancel** (Pending), **checkout** Stripe (`{id}/checkout`), `**POST /api/Order/{id}/refund`** (policy `order.refund`) — hoàn tiền đơn **Paid**; idempotency Stripe `refund-order-{id}`. **GET /api/Order/my-orders** (policy `order.read`): đơn của user đăng nhập qua `GetOrdersForCurrentUserAsync`. **GET /api/Order/user/{userId}**: **AdminOnly** — danh sách đơn theo `userId`. Danh sách phân trang: **GET /api/Order** (Admin).
- **Roles / Permissions**: quản trị vai trò và quyền.
- **Users** (Admin): danh sách user + role, gán role (`IRoleService`).
- **Devices** (`api/devices`): thiết bị/phiên qua **IDeviceSessionService** (liệt kê, revoke một thiết bị, revoke tất cả). **Lưu ý:** toàn bộ controller đang gắn `[Authorize(Policy = AdminOnly)]` cùng `[Authorize]` — logic handler dùng `userId` từ token (giống self-service) nhưng **chỉ Admin** mới gọi được API; cần tách policy nếu muốn user thường tự quản thiết bị.
- **Webhooks**: `POST /api/webhooks/stripe` (anonymous, ký Stripe).

### Bảo mật & identity

- JWT + **refresh token rotation**; **IP binding** (`iph`), **session** (`sid` / `sv`); **token blacklist** (Redis).
- Phân quyền **policy** theo permission + role **Admin** (Admin bypass permission qua handler).
- Swagger: **SwaggerSecurityRequirementsOperationFilter** gắn Bearer + **X-Device-Id** cho operation có `[Authorize]` (tránh gọi secured API không kèm token).

### Catalog & đơn hàng

- Product/Category: pagination, dynamic sorting, prefix search; rule nghiệp vụ (vd. không xóa category còn product).
- Order: tạo đơn + trừ kho trong transaction (kể cả khi nguồn là cart); **optimistic concurrency** trên **Order** và **Product** qua PostgreSQL **xmin** (`UseXminAsConcurrencyToken`).
- Validate số lượng theo **Stock**; gom dòng trùng `ProductId` khi tạo đơn; double-check tồn kho khi checkout từ giỏ (logic `CreateOrderAsync`).
- **Cancel**: chỉ **Pending**; hoàn kho; `Cancelled` + `PaymentStatus.Cancelled`; Admin có thể hủy đơn người khác.
- **Refund (đã Paid)**: Stripe full refund; sau refund: **OrderStatus.Cancelled**, **PaymentStatus.Refunded**, hoàn kho; lưu **StripeRefundId**.

### Thanh toán (Stripe)

- **PaymentIntent**, metadata `orderId`; idempotency checkout (`order-{id}-usd{cents}-v2` / `order-{id}-retry-{guid}` khi **Failed**); **GetReusablePaymentIntentAsync** khi Pending + đã có PI.
- **Refund**: `CreateRefundForPaymentIntentAsync` + webhook **charge.refunded** / **refund.updated**; đồng bộ **HandleRefundCompletedAsync** (idempotent với API refund).
- Webhook thanh toán: `payment_intent.succeeded` / `failed`, `charge.succeeded` / `failed`; đồng bộ **OrderStatus** / **PaymentStatus**.
- **GlobalExceptionMiddleware**: `StripeException` → 502 + `STRIPE_ERROR`.
- Cấu hình **Stripe__** / alias **STRIPE_** (`EnvLoader`, `.env.example`); **docker-compose** inject Stripe vào service `api`.

### Enums & Order entity

- **OrderStatus**: Pending, Paid, Shipped, Delivered, Cancelled.
- **PaymentStatus**: Pending, Succeeded, Failed, Cancelled, **Refunded**.
- **Order**: `StripePaymentIntentId`, **StripeRefundId**; cập nhật sau thanh toán / refund qua API và webhook.

### Hạ tầng & DevOps

- Cache-aside (Redis hoặc memory), versioning list product, cache order.
- Serilog (console + file), `RequestId`.
- **Docker Compose**: API **5169**, Postgres **db** + **db-local** (host **5434**, `InternProjectDb_Local`), Redis; task VS Code **Docker Redeploy**, **EF Reset Database**.

### Unit tests (`Ecommerce.UnitTests`)

- Auth, Jwt, Product, Category, Role, Permission, Order (tạo đơn, cancel, concurrency, checkout, **RefundPaidOrderAsync** / **HandleRefundCompleted**, payment succeeded khi đã refunded, **HandlePaymentSucceeded/Failed**, reuse PI, retry idempotency khi Failed, **GetOrdersForCurrentUserAsync**, **GetOrdersByUserIdAsync**).
- **Payment / webhook**: **WebhookControllerTests**, **StripePaymentServiceTests** (`ValidateWebhookSignature`), stub **TestPaymentWebhookStub**.
- **HttpRequestDeviceContextTests** (device + **UserId** từ claim).
- Project test tham chiếu **Application**, **Infrastructure**, **Ecommerce.API.csproj** (root) để test controller/webhook.
- **Chưa có** unit test riêng cho **CartService** (roadmap tùy chọn).

---

## 2. Những gì cần làm thêm (To-do / Roadmap)

- **Cart**: (tùy chọn) unit test `CartService`; cảnh báo giá cart vs giá hiện tại ở UI; xử lý khi Redis down rõ ràng hơn nếu cần SLA.
- **Fulfillment**: có enum **Shipped** / **Delivered** nhưng chưa API/service chuyển trạng thái giao hàng rõ ràng.
- **User (self-service)**: bổ sung **profile** (ngoài `me`), **đổi mật khẩu**, **quên mật khẩu** (email).
- **Reviews & ratings** sản phẩm.
- **Dashboard / analytics** (Admin).
- **Policy DeviceController**: bỏ **AdminOnly** toàn lớp hoặc tách endpoint Admin vs user (hiện revoke/list theo `userId` token nhưng policy chặn non-Admin).
- **Build / cảnh báo**: đồng bộ phiên bản package (EF Tools **8.0.24**, Npgsql EF **8.0.11**, …); build có thể còn cảnh báo transitive **Microsoft.Extensions.** preview so với **net8.0** — xem xét pin phiên bản ổn định hoặc `SuppressTfmSupportBuildWarnings` nếu cố ý dùng preview.

---

## 3. Tối ưu hóa (Optimizations)

### Code & maintainability

- Tách `LogStripeConfigurationStatus` ra extension nếu muốn `Program.cs` đồng nhất hoàn toàn.
- DRY giữa **ProductService** / **CategoryService** (phân trang, cache version).
- **FluentValidation** thay/thêm DataAnnotations cho DTO phức tạp.

### Hiệu năng

- L1 cache trước Redis cho đọc nóng.
- Dapper / SQL thuần cho báo cáo lớn.

### Bảo mật

- **Rate limiting** (`AddRateLimiter`) cho login/register và endpoint nhạy cảm — **chưa** thấy trong `Program.cs`.
- **CORS** policy rõ ràng theo môi trường — **chưa** cấu hình chi tiết.

---

## 4. Kết luận

Hệ thống đã có **auth/session nâng cao**, **giỏ hàng Redis + đặt hàng từ giỏ**, **đơn hàng + concurrency (xmin)**, **Stripe checkout + webhook**, **refund API + reconcile webhook**, **quản trị role/permission/user (Admin)**, **API thiết bị/phiên** (đang giới hạn Admin), và **unit test** tương đối đầy đủ kể cả payment/refund/webhook. Để gần sàn TMĐT hoàn chỉnh còn **fulfillment**, **self-service user đầy đủ**, **reviews**, **analytics**, **test Cart** (nếu muốn), và **hardening** (rate limit, CORS, tinh chỉnh policy thiết bị).