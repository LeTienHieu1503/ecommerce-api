# Ecommerce API

A RESTful e-commerce API built with **ASP.NET Core 8**, supporting product management, order processing, **Stripe** payment integration (including webhooks), **JWT authentication**, role/permission-based authorization, and device binding.

## Key Features

* **Categories & Products**: Structured CRUD operations via API.
* **Orders**: Order workflow, payment status tracking, Stripe integration (checkout / refund based on current implementation).
* **Stripe Webhook**: Updates payment status from Stripe events (`WebhookController`).
* **Authentication & Authorization**: JWT-based authentication, user/role/permission management.
* **Device Handling**: APIs related to device context (`DeviceController`).
* **OpenAPI / Swagger**: API documentation available in Development environment.
* **Logging**: Serilog (console + rolling file logs in `logs/` directory).
* **Unit Tests**: `Ecommerce.UnitTests` project (xUnit + FluentAssertions).

## Architecture

The solution follows a **layered architecture**:

| Project                    | Responsibility                                                            |
| -------------------------- | ------------------------------------------------------------------------- |
| `Ecommerce.API`            | Web API host, middleware, controllers, dependency injection configuration |
| `Ecommerce.Application`    | DTOs, service interfaces, mappers, application logic                      |
| `Ecommerce.Domain`         | Entities, enums, core business rules                                      |
| `Ecommerce.Infrastructure` | EF Core, PostgreSQL, Redis, Stripe, migrations                            |
| `Ecommerce.UnitTests`      | Testing                                                                   |

## Technologies

* .NET 8, ASP.NET Core Web API
* Entity Framework Core + **PostgreSQL** (Npgsql)
* **Redis** (StackExchange.Redis) — configurable via settings
* JWT Bearer Authentication
* **Stripe.net**
* Serilog, Swashbuckle (Swagger)
* Docker Compose (API + PostgreSQL + Redis)

## Prerequisites

* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* PostgreSQL (or use Docker Compose)
* Redis (optional, depending on `Redis:Enabled`)
* A [Stripe](https://stripe.com) account (test keys + webhook secret for development)

## Quick Start (Local)

1. **Clone the repository**

   ```bash
   git clone https://github.com/LeTienHieu1503/ecommerce-api.git
   cd Ecommerce.API
   ```

2. **Configure connection strings** in `appsettings.json` or `appsettings.Development.json`
   (`ConnectionStrings:DefaultConnection`, `ConnectionStrings:Redis`).

3. **Environment Variables / Stripe / JWT**

   * Copy `.env.example` to `.env` in the solution root (same level as `Ecommerce.API.sln`).
   * Fill in values like `Stripe__SecretKey`, `Stripe__WebhookSecret`, etc.
   * The application uses `EnvLoader` to load `.env` at startup.

4. **Run the API**

   ```bash
   dotnet run --project Ecommerce.API.csproj
   ```

   By default, the **https** profile points to Swagger, e.g.:
   `https://localhost:7041/swagger` (see `Properties/launchSettings.json`).

5. **Database Migration**
   The application calls `Database.MigrateAsync()` on startup (with retry).
   Admin seeding (roles/permissions) runs after migration.

## Docker Compose

Run API with PostgreSQL and Redis:

```bash
docker compose up -d --build
```

* API typically maps port **5169** → container port 80 (see `docker-compose.yml`).
* Ensure `.env` variables are properly configured (e.g., `POSTGRES_PASSWORD`, `Jwt__Key`, Stripe secrets if using payments).
* For local Stripe webhook testing: use Stripe CLI to forward events to your local endpoint (see `.env.example`).

## Run Tests

```bash
dotnet test Ecommerce.API.sln
```
