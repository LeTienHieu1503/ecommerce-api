# Kế hoạch thực hiện Refund (Stripe + Order) — đã chốt

Tài liệu **execution plan**: các quyết định nghiệp vụ và kỹ thuật đã được **chốt** để triển khai theo thứ tự, không còn nhánh “tuỳ chọn” mở.

---

## A. Quyết định đã chốt (tóm tắt)

| # | Chủ đề | Quyết định |
|---|--------|------------|
| 1 | Trạng thái sau refund | **`OrderStatus.Cancelled`** + **`PaymentStatus.Refunded`** |
| 2 | Ai được gọi API refund | Endpoint dùng policy **`order.refund`** (cùng pattern `order.delete`, …). **`PermissionHandler`**: user có role **`Admin`** → **luôn pass** mọi policy permission (không đọc DB). User **không** phải Admin → cần có **`order.refund`** trong quyền lấy từ `GetUserPermissionsAsync` (thường qua `RolePermissions`). Bảng `Permissions` phải có dòng **`order.refund`** để gán cho role vận hành (Admin không bắt buộc có dòng này trên role để gọi API). |
| 3 | Đơn Shipped / Delivered | **Không** cho refund (từ chối với `BusinessException` rõ ràng). Chỉ **`OrderStatus.Paid`** được refund. |
| 4 | Phạm vi tiền | **Hoàn toàn bộ** theo PaymentIntent (không amount từ client trong MVP). |
| 5 | Idempotency Stripe | Key cố định **`refund-order-{orderId}`** (cùng key + cùng request → Stripe idempotent; phù hợp client retry). |
| 6 | Audit | Lưu **`StripeRefundId`** (`re_...`) trên `Order` sau khi Stripe trả refund id. |
| 7 | Đồng bộ bất đồng bộ | Webhook **`charge.refunded`** và **`refund.updated`** (chỉ xử lý khi refund ở trạng thái thành công / tương đương). |
| 8 | Gọi lại API refund khi đã refund | **Idempotent 200**: trả `OrderDto` hiện tại, không gọi Stripe lần 2. |

---

## B. Điều kiện nghiệp vụ (refund qua API)

Tất cả phải đúng, nếu không → `BusinessException` / `NotFoundException` tương ứng:

1. Order tồn tại; caller có permission **`order.refund`** (handler permission như các API order khác).
2. **`OrderStatus == Paid`** (và **không** `Shipped` / `Delivered` / `Cancelled` — đã bao phủ bởi chỉ cho `Paid`).
3. **`PaymentStatus == Succeeded`**.
4. **`StripePaymentIntentId`** không rỗng.
5. Chưa refund: **`PaymentStatus != Refunded`** (và có thể kiểm tra `StripeRefundId` null nếu muốn chặt).

---

## C. Luồng thực hiện (happy path)

### C.1 API `POST /api/Order/{id}/refund`

1. `[Authorize(Policy = "order.refund")]`.
2. Load order kèm **Items** (tracked), validate mục B.
3. Nếu đã **`Refunded`**: trả **200** + DTO (idempotent), **không** gọi Stripe.
4. Gọi **`IPaymentService.CreateRefundForPaymentIntentAsync(paymentIntentId, idempotencyKey: $"refund-order-{orderId}")`**.
5. Stripe lỗi → **không** commit thay đổi nghiệp vụ DB (log + trả lỗi; middleware có thể map `StripeException` → 502).
6. Stripe OK → trong **một transaction**:
   - `PaymentStatus = Refunded`
   - `OrderStatus = Cancelled`
   - `StripeRefundId = <id từ Stripe>`
   - Hoàn kho: cùng logic với **`CancelOrderAsync`** (duyệt `OrderItem`, `Product.Stock += Quantity`).
7. `SaveChanges`, invalidate cache order + product (giống cancel).

### C.2 Webhook (reconcile / late events)

1. Mở rộng **`ValidateWebhookSignature`** để lấy `payment_intent` từ payload **`charge.refunded`** / **`refund.updated`** (cast Stripe object + fallback JSON như charge hiện tại).
2. **`WebhookController`**: thêm `case` gọi **`IOrderService.HandleRefundCompletedAsync(paymentIntentId)`** (hoặc tên tương đương).
3. Handler: tìm order theo `StripePaymentIntentId`; nếu đã `Refunded` → return; nếu vẫn `Paid` + `Succeeded` → áp dụng **cùng cập nhật DB + hoàn kho** như C.1 bước 6 (không gọi Stripe từ webhook — tránh double refund). *Lưu ý:* nếu API đã lưu `StripeRefundId` mà webhook tới sau, chỉ cần idempotent no-op hoặc bổ sung id nếu thiếu.

---

## D. Thay đổi Domain & DB

| Thành phần | Việc làm |
|------------|----------|
| `PaymentStatus` | Thêm **`Refunded = 4`**. |
| `Order` | Thêm **`StripeRefundId`** (nullable, string, max ~255), index tuỳ chọn. |
| EF migration | Một migration: enum + cột mới. |
| `OrderConfiguration` | Cấu hình property `StripeRefundId`. |
| `OrderDto` + `OrderMapper` | Map `StripeRefundId`; hiển thị `PaymentStatus` (đã có thể cần format string). |

---

## E. Application / Infrastructure — chi tiết file

| Lớp | Việc làm |
|-----|----------|
| `IPaymentService` | Thêm `RefundCreateResult` (vd. `RefundId`) + `CreateRefundForPaymentIntentAsync(...)`. |
| `StripePaymentService` | `RefundService.CreateAsync` với `PaymentIntent`, `RequestOptions.IdempotencyKey`. |
| `IOrderService` | `Task<OrderDto> RefundPaidOrderAsync(int orderId, CancellationToken ct = default)` — *không cần `currentUserId` nếu authorization chỉ ở controller; hoặc truyền để log audit.* |
| `OrderService` | Implement refund + tái sử dụng hoàn kho (nên extract **`RestoreStockForOrderItemsAsync`** dùng chung với `CancelOrderAsync` để DRY). |
| `GlobalExceptionMiddleware` | Giữ xử lý `StripeException` nếu refund lỗi từ Stripe. |

---

## F. API

- **`POST /api/Order/{id:int}/refund`**
- **`[Authorize(Policy = "order.refund")]`**
- Body: **không** (MVP).
- Response: **`OrderDto`** (hoặc wrapper success hiện có của `BaseApiController`).

---

## G. Auth & permission trong DB

- **`AuthExtensions`** đã khai báo policy **`order.refund`** (cùng mảng permission với `order.delete`, …).
- **`PermissionHandler`**: **`Admin`** → `Succeed` (mọi policy dạng permission); không phải Admin → `GetUserPermissionsAsync` phải chứa **`order.refund`**.
- **`PolicyPermissionSeeder.EnsurePolicyPermissionsAsync`** (sau migrate trong `Program.cs`): insert **if missing** các tên permission **`order.*`** dùng cho policy (gồm **`order.refund`**) để gán qua `RolePermissions`. Admin không cần bản ghi `RolePermissions` cho **`order.refund`** để gọi API.

---

## H. Stripe (Dashboard / CLI)

- Endpoint webhook: **`POST /api/webhooks/stripe`** (như hiện tại).
- Subscribe thêm: **`charge.refunded`**, **`refund.updated`**.
- Dev: `stripe listen --forward-to <base>/api/webhooks/stripe`.

---

## I. Unit test (tối thiểu)

| Case | Nơi |
|------|-----|
| Refund thành công → `Cancelled`, `Refunded`, stock restored, `StripeRefundId` set | `OrderServiceTests` |
| Đã `Refunded` → gọi lại → không gọi `CreateRefund*` lần 2 | `OrderServiceTests` |
| `Shipped` / `Delivered` / không Paid → `BusinessException` | `OrderServiceTests` |
| Stripe throw → DB không đổi (mock) | `OrderServiceTests` |
| Webhook `charge.refunded` / `refund.updated` → gọi handler | `WebhookControllerTests` (stub như hiện tại) |

---

## J. Rủi ro (đã ghi nhận, không đổi scope MVP)

- Refund Stripe OK nhưng DB lỗi sau đó → webhook **HandleRefundCompleted** reconcile.
- PaymentIntent chưa capture (lệch với flow hiện tại) → xử lý sau nếu có thay đổi capture.

---

## K. Thứ tự triển khai (checklist)

1. [x] Domain: `PaymentStatus.Refunded`, `Order.StripeRefundId` + **migration**.
2. [x] EF configuration + snapshot.
3. [x] `IPaymentService` + `StripePaymentService` (refund + idempotency key).
4. [x] Extract **hoàn kho** dùng chung; `RefundPaidOrderAsync` + idempotent branch.
5. [x] `HandleRefundCompletedAsync` + mở rộng **`ValidateWebhookSignature`** + **`WebhookController`**.
6. [x] `OrderController` endpoint + policy **`order.refund`**.
7. [x] **`PolicyPermissionSeeder`** — permission **`order.refund`** (và các `order.*` policy) insert if missing.
8. [x] `OrderDto` / mapper.
9. [x] Unit tests (mục I) + `dotnet test` full solution.
10. [x] Cập nhật `docs/analysis_report.md`.
11. [ ] Stripe Dashboard: subscribe **`charge.refunded`**, **`refund.updated`** trên endpoint deploy.

---

*Tài liệu này thay thế phần “đề xuất / cần chốt” trước đây; mọi thay đổi nghiệp vụ sau MVP nên sửa trực tiếp file này hoặc tách phase 2.*
