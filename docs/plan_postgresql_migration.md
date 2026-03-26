# Kế hoạch chuyển SQL Server → PostgreSQL

Tài liệu này dựa trên **rà soát codebase** tại thời điểm tạo. Phạm vi: dự án cá nhân, dữ liệu có thể bỏ, chưa deploy production.

---

## 1. Kết quả rà soát nhanh

| Hạng mục | Trạng thái |
|----------|------------|
| EF Core | 8.0.x, provider `Microsoft.EntityFrameworkCore.SqlServer` trong `Ecommerce.Infrastructure/Ecommerce.Infrastructure.csproj` |
| Đăng ký DbContext | `Extensions/InfrastructureExtensions.cs` — `UseSqlServer` + `DefaultConnection` |
| Raw SQL (`FromSqlRaw`, `ExecuteSqlRaw`, …) | **Không có** trong mã nguồn ứng dụng (chỉ migration hiện tại chứa T-SQL/SqlServer annotations) |
| `ApplicationDbContext` | Không có API chỉ dành cho SQL Server; `DateTime.UtcNow` trong `SaveChangesAsync` — tương thích Postgres |
| Fluent configurations | Chỉ `decimal(18,2)` — EF map được sang Postgres |
| Docker | `docker-compose.yml`: service `db` = `mcr.microsoft.com/mssql/server:2022-latest`, volume `sql_data`, healthcheck `sqlcmd` |
| Connection strings | `appsettings.json` (Windows auth local); compose override `ConnectionStrings__DefaultConnection` trỏ `Server=db` |
| EF Tools | `Microsoft.EntityFrameworkCore.Tools` trong `Ecommerce.API.csproj` (đủ cho `dotnet ef`) |
| `IDesignTimeDbContextFactory` | **Không có** — migration dùng startup project + `appsettings` |

**Kết luận:** Logic service/repository qua LINQ **không cần viết lại**. Công việc chính: đổi provider EF, connection string, Docker, và **tạo lại migration** cho Npgsql (migration SQL Server hiện tại không áp dụng trực tiếp lên Postgres).

---

## 2. Các file / vị trí sẽ thay đổi

| Vị trí | Việc cần làm |
|--------|----------------|
| `Ecommerce.Infrastructure/Ecommerce.Infrastructure.csproj` | Gỡ `Microsoft.EntityFrameworkCore.SqlServer`; thêm `Npgsql.EntityFrameworkCore.PostgreSQL` (khuyến nghị **8.0.x** trùng major EF) |
| `Extensions/InfrastructureExtensions.cs` | `UseSqlServer(...)` → `UseNpgsql(...)` |
| `appsettings.json` | `DefaultConnection` → chuỗi Npgsql (local dev) |
| `appsettings.Development.json` | (Tùy chọn) thêm `DefaultConnection` nếu dev không kế thừa từ base |
| `docker-compose.yml` | Thay image DB, env, port, healthcheck; đổi `ConnectionStrings__DefaultConnection`; đổi biến môi trường `.env` (bỏ `MSSQL_SA_PASSWORD`, dùng `POSTGRES_*`) |
| `Ecommerce.Infrastructure/Migrations/*` | **Xóa toàn bộ** rồi `migrations add InitialCreate` + `database update` (vì không cần giữ lịch sử SQL Server) |
| `.env` (nếu dùng) | Thay biến cho Postgres; **không commit** secret |

**Không bắt buộc đổi:** `Dockerfile` (chỉ .NET runtime), Redis, JWT — trừ khi có script khởi tạo DB riêng (hiện không thấy).

---

## 3. Thứ tự thực hiện (checklist)

### Bước A — NuGet & code

1. Trong `Ecommerce.Infrastructure.csproj`: thay package SqlServer bằng Npgsql (version align EF 8).
2. Sửa `InfrastructureExtensions.cs`: `UseNpgsql(configuration.GetConnectionString("DefaultConnection"))`.
3. Cập nhật `appsettings.json` mẫu local, ví dụ:
   - `Host=localhost;Port=5432;Database=InternProjectDb;Username=...;Password=...`
4. Build solution: `dotnet build Ecommerce.API.sln`.

### Bước B — Migration mới (DB trống / bỏ data)

1. Dừng container DB cũ nếu đang chạy; (tùy chọn) `docker volume rm` volume SQL cũ để tránh nhầm.
2. Xóa hết file trong `Ecommerce.Infrastructure/Migrations/` (gồm `ApplicationDbContextModelSnapshot.cs`).
3. Từ thư mục chứa solution:
   ```bash
   dotnet ef migrations add InitialCreate --project Ecommerce.Infrastructure --startup-project Ecommerce.API
   dotnet ef database update --project Ecommerce.Infrastructure --startup-project Ecommerce.API
   ```
4. Rà migration sinh ra: bảng, FK, unique index trên `User.Email` (đã khai báo trong `OnModelCreating`).

### Bước C — Docker Compose

1. Service `db`:
   - Image: ví dụ `postgres:16-alpine` (hoặc bản team chọn).
   - Env: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` (vd. `InternProjectDb`).
   - Port: `5432:5432`.
   - Volume: đổi tên volume (vd. `postgres_data`) cho rõ ràng.
   - Healthcheck: `pg_isready -U $POSTGRES_USER -d $POSTGRES_DB` (hoặc tương đương).
2. Service `api` — `ConnectionStrings__DefaultConnection` ví dụ:
   - `Host=db;Port=5432;Database=InternProjectDb;Username=...;Password=...`
3. Cập nhật `.env`: thay `MSSQL_SA_PASSWORD` bằng biến Postgres (vd. `POSTGRES_PASSWORD`); chỉnh compose để substitute đúng.

### Bước D — Xác minh

1. `docker compose up --build` (hoặc flow hiện tại của bạn).
2. Gọi Swagger: đăng ký/đăng nhập, CRUD chính (order/product/category).
3. Chạy unit test: `dotnet test` (project `Ecommerce.UnitTests` nếu có).

---

## 4. Rủi ro & lưu ý

- **Không** chạy lại file migration SQL Server trên Postgres — luôn dùng migration mới do Npgsql tạo.
- Nếu sau này cần **giữ dữ liệu** SQL Server → Postgres: cần bước ETL/backup riêng; ngoài phạm vi plan “data không quan trọng”.
- Collation/sort string: mặc định Postgres khác SQL Server; với use case thông thường của API ecommerce thường không chặn, nhưng nếu có so sánh case-sensitive đặc biệt cần test thêm.

---

## 5. Ước lượng effort (dự án cá nhân, không data)

| Công việc | Thời gian tham khảo |
|-----------|---------------------|
| Package + `UseNpgsql` + appsettings | ~15–30 phút |
| Xóa migration cũ + add + update | ~15–30 phút |
| Docker Compose + `.env` + smoke test | ~30–60 phút |

Tổng: khoảng **1–2 giờ** nếu không vướng môi trường local.

---

## 6. Sau khi hoàn tất (tùy chọn)

- Cập nhật `docs/analysis_report.md` hoặc README (khi có) ghi nhận stack DB mới.
- Commit message rõ ràng: ví dụ `chore: migrate database from SQL Server to PostgreSQL`.

---
