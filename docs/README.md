# Ecommerce API

REST API thương mại điện tử xây dựng bằng **ASP.NET Core 8**, hỗ trợ quản lý sản phẩm, đơn hàng, thanh toán **Stripe** (kèm webhook), xác thực **JWT**, phân quyền theo vai trò/quyền và binding thiết bị.

## Tính năng chính

- **Danh mục & sản phẩm**: CRUD qua API có tổ chức.
- **Đơn hàng**: Luồng đặt hàng, trạng thái thanh toán, tích hợp Stripe (checkout / hoàn tiền theo mã nguồn hiện tại).
- **Webhook Stripe**: Cập nhật trạng thái thanh toán từ sự kiện Stripe (`WebhookController`).
- **Xác thực & phân quyền**: JWT, quản lý user, role, permission.
- **Thiết bị**: API liên quan thiết bị / ngữ cảnh request (`DeviceController`).
- **OpenAPI / Swagger**: Tài liệu API khi chạy môi trường Development.
- **Logging**: Serilog (console + file luân phiên trong thư mục `logs/`).
- **Unit tests**: Project `Ecommerce.UnitTests` (xUnit + FluentAssertions).

## Kiến trúc

Solution theo hướng **tách lớp**:

| Project | Vai trò |
|--------|---------|
| `Ecommerce.API` | Host Web API, middleware, controllers, cấu hình DI |
| `Ecommerce.Application` | DTO, interface service, mapper, logic ứng dụng |
| `Ecommerce.Domain` | Entity, enum, quy tắc nghiệp vụ cốt lõi |
| `Ecommerce.Infrastructure` | EF Core, PostgreSQL, Redis, Stripe, migration |
| `Ecommerce.UnitTests` | Kiểm thử |

## Công nghệ

- .NET 8, ASP.NET Core Web API  
- Entity Framework Core + **PostgreSQL** (Npgsql)  
- **Redis** (StackExchange.Redis) — có thể bật/tắt qua cấu hình  
- JWT Bearer authentication  
- **Stripe.net**  
- Serilog, Swashbuckle (Swagger)  
- Docker Compose (API + Postgres + Redis)

## Yêu cầu môi trường

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)  
- PostgreSQL (hoặc dùng Docker Compose)  
- Redis (tùy cấu hình `Redis:Enabled`)  
- Tài khoản [Stripe](https://stripe.com) (test keys + webhook secret khi phát triển webhook)

## Chạy nhanh (local)

1. **Clone repository**

   ```bash
   git clone <url-repo-của-bạn>
   cd Ecommerce.API
   ```

2. **Cấu hình kết nối** trong `appsettings.json` hoặc `appsettings.Development.json` (mục `ConnectionStrings:DefaultConnection`, `ConnectionStrings:Redis`).

3. **Biến môi trường / Stripe / JWT**  
   - Sao chép `.env.example` thành `.env` tại thư mục gốc solution (cùng cấp với `Ecommerce.API.sln`).  
   - Điền `Stripe__SecretKey`, `Stripe__WebhookSecret`, v.v. theo hướng dẫn trong file.  
   - Ứng dụng dùng `EnvLoader` để nạp `.env` khi khởi động.

4. **Chạy API**

   ```bash
   dotnet run --project Ecommerce.API.csproj
   ```

   Mặc định có profile **https** trỏ tới Swagger, ví dụ: `https://localhost:7041/swagger` (xem `Properties/launchSettings.json`).

5. **Migration**  
   Ứng dụng gọi `Database.MigrateAsync()` khi start (có retry). Seed admin/quyền cũng chạy sau migration.

## Docker Compose

Chạy API cùng Postgres và Redis:

```bash
docker compose up -d --build
```

- API thường map cổng **5169** → container port 80 (xem `docker-compose.yml`).  
- Cần biến trong `.env` phù hợp (ví dụ `POSTGRES_PASSWORD`, `Jwt__Key`, secret Stripe nếu dùng thanh toán).  
- Webhook Stripe local: dùng Stripe CLI forward tới URL tương ứng (gợi ý trong `.env.example`).

## Chạy tests

```bash
dotnet test Ecommerce.API.sln
```

