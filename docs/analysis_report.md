# Báo cáo Phân tích Dự án Ecommerce.API

Dựa trên rà soát mã nguồn hiện tại (controllers, services, test, cấu hình), báo cáo tóm tắt trạng thái dự án, roadmap và hướng tối ưu.

## 1. Những gì đã làm được (Completed)

Dự án theo **Clean Architecture** (Domain / Application / Infrastructure / API), có **Stripe** cho thanh toán và **Redis** cho cache & blacklist token.

### Kiến trúc & pattern

- **Repository + Unit of Work**: transaction `BeginTransactionAsync()` cho luồng cần ACID (tạo đơn, hủy đơn, …).
- **Mapper tĩnh** (không AutoMapper).
- **BaseApiController + ApiResponse&lt;T&gt;**: format response thống nhất.
- **`Program.cs`**: cấu hình qua `Extensions` (`AddInfrastructure`, `AddApplicationServices`, `AddAuthConfig`, `AddSwaggerConfig`); **`IRequestDeviceContext`** + **`HttpRequestDeviceContext`** + `AddHttpContextAccessor` cho ngữ cảnh request/thiết bị.

### API (Controllers)

- **Auth**: đăng nhập, refresh, đăng xuất, …
- **Category / Product**: CRUD, phân trang, sort, tìm kiếm.
- **Order**: tạo đơn, chi tiết, danh sách (Admin), theo user, **cancel** (Pending), **checkout** (Stripe).
- **Roles / Permissions**: quản trị vai trò và quyền.
- **Users** (Admin): danh sách user + role, gán role (`IRoleService`).
- **Devices** (`api/devices`): thiết bị/phiên đăng nhập qua **`IDeviceSessionService`** (liệt kê, revoke một thiết bị, revoke tất cả). Controller đang gắn **`[Authorize(AdminOnly)]`** — cần khớp với yêu cầu sản phẩm (user tự xem thiết bị vs chỉ Admin).
- **Webhooks**: `POST /api/webhooks/stripe` (anonymous, ký Stripe).

### Bảo mật & identity

- JWT + **refresh token rotation**; **IP binding** (`iph`), **session** (`sid` / `sv`); **token blacklist** (Redis).
- Phân quyền **policy** theo permission + role **Admin**.

### Catalog & đơn hàng

- Product/Category: pagination, dynamic sorting, prefix search; rule nghiệp vụ (vd. không xóa category còn product).
- Order: tạo đơn + trừ kho trong transaction; **optimistic concurrency** trên **Order** và **Product** qua PostgreSQL **`xmin`** (`UseXminAsConcurrencyToken`, không phải property `RowVersion` trên entity).
- Validate số lượng theo **Stock**; gom dòng trùng `ProductId` khi tạo đơn.
- **Cancel**: chỉ **Pending**; hoàn kho; `Cancelled` + `PaymentStatus.Cancelled`; Admin có thể hủy đơn người khác.

### Thanh toán (Stripe)

- **PaymentIntent**, metadata `orderId`; idempotency (`order-{id}-usd{cents}-v2` / `order-{id}-retry-{guid}` khi **Failed**); **`GetReusablePaymentIntentAsync`** khi Pending + đã có PI.
- Webhook: `payment_intent.succeeded` / `failed`, `charge.succeeded` / `failed`; trích `payment_intent` (cast + fallback JSON); đồng bộ **OrderStatus** / **PaymentStatus**.
- **GlobalExceptionMiddleware**: `StripeException` → 502 + `STRIPE_ERROR`.
- Cấu hình **`Stripe__*`** / alias **`STRIPE_*`** (`EnvLoader`, `.env.example`); **docker-compose** inject Stripe vào service `api`.

### Enums & Order entity

- **OrderStatus**: Pending, Paid, Shipped, Delivered, Cancelled.
- **PaymentStatus**: Pending, Succeeded, Failed, Cancelled.
- **Order**: `StripePaymentIntentId`; cập nhật sau thanh toán qua webhook.

### Hạ tầng & DevOps

- Cache-aside (Redis hoặc memory), versioning list product, cache order.
- Serilog (console + file), `RequestId`.
- **Docker Compose**: API **5169**, Postgres (`db`, `db-local`), Redis; task VS Code **Docker Redeploy**, **EF Reset Database**.

### Unit tests (`Ecommerce.UnitTests`)

- Auth, Jwt, Product, Category, Role, Permission, Order (tạo đơn, cancel, concurrency, **checkout**, **HandlePaymentSucceeded/Failed**, reuse PI, **retry idempotency khi Failed**).
- **Payment bổ sung**: **`WebhookControllerTests`**, **`StripePaymentServiceTests`** (`ValidateWebhookSignature`: chữ ký rỗng/null/sai HMAC), stub **`TestPaymentWebhookStub`**.
- Project test tham chiếu **Application**, **Infrastructure**, **Ecommerce.API.csproj** (root) để test controller/webhook.

---

## 2. Những gì cần làm thêm (To-do / Roadmap)

- **Giỏ hàng**: vẫn đặt hàng trực tiếp; chưa `CartService` (Redis/DB).
- **Refund đơn đã Paid**: đã có **`POST /api/Order/{id}/refund`** (policy **`order.refund`**), Stripe full refund + webhook **`charge.refunded`** / **`refund.updated`**. *Hủy sau thanh toán* theo nghiệp vụ hiện tại = refund (trạng thái **Cancelled** + **Refunded**).
- **Fulfillment**: có enum **Shipped** / **Delivered** nhưng chưa API/service chuyển trạng thái giao hàng rõ ràng.
- **User (phía end-user)**: đã có **Admin** quản user/role; thiếu **profile**, **đổi mật khẩu**, **quên mật khẩu** (email).
- **Reviews & ratings** sản phẩm.
- **Dashboard / analytics** (Admin).
- **Policy DeviceController**: xem lại **AdminOnly** vs user tự quản lý thiết bị của mình.
- **Test / build**: có thể giảm cảnh báo **MSB3277** (phiên bản EF Core lệch giữa Tools/Infrastructure) khi tham chiếu API từ test project; hoặc đồng bộ package EF.

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

Hệ thống đã có **auth/session nâng cao**, **đơn hàng + concurrency (xmin)**, **Stripe checkout + webhook** (kể cả **refund** reconcile), **quản trị role/permission/user (Admin)**, **API thiết bị/phiên**, và **bộ unit test tương đối đầy đủ** kể cả payment/webhook. Để gần sàn TMĐT hoàn chỉnh còn **cart**, **fulfillment**, **self-service user**, **reviews**, **analytics**, và **hardening** (rate limit, CORS, tinh chỉnh policy thiết bị).
