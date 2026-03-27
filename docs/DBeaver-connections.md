# Kết nối DBeaver — hai Postgres (Docker API vs Local API)

Cùng mật khẩu: giá trị `POSTGRES_PASSWORD` trong file `.env` (Compose dùng cho cả hai container).

| | **Docker API** (`db`) | **Local** (`dotnet run`, `db-local`) |
|---|------------------------|--------------------------------------|
| **Host** | `localhost` | `localhost` |
| **Port** | `5432` | `5434` |
| **Database** | `InternProjectDb` | `InternProjectDb_Local` |
| **Username** | `postgres` | `postgres` |
| **Password** | `POSTGRES_PASSWORD` trong `.env` | `POSTGRES_PASSWORD` trong `.env` |

Trong DBeaver: tạo hai connection PostgreSQL, đặt tên rõ (ví dụ `Ecommerce-Docker-5432` và `Ecommerce-Local-5434`).

## Khởi động container

```bash
docker compose up -d db db-local redis
```

Lần đầu, chạy API local một lần (hoặc `dotnet ef database update`) để tạo schema trên `InternProjectDb_Local`.
