# Agent Task: Device Session Improvement Plan
> Dự án: Ecommerce.API (.NET 8, Clean Architecture)  
> Thời gian ước tính: 1 ngày (8 tiếng)  
> Test: Swagger/Postman (không có FE)

---

## Quy tắc bắt buộc cho Agent

- Đọc kỹ từng task trước khi thực hiện, KHÔNG skip bước nào
- Sau mỗi task hoàn thành, chạy `dotnet build` để xác nhận không có lỗi compile
- Nếu một file chưa tồn tại, tạo mới. Nếu đã tồn tại, chỉnh sửa đúng chỗ, KHÔNG xóa code cũ trừ khi task yêu cầu rõ ràng
- Tuân thủ đúng naming convention hiện tại của project (xem các file có sẵn trước khi tạo mới)
- Mỗi interface đặt trong `Application/Interfaces/`, implementation trong `Infrastructure/Services/`
- Mỗi entity đặt trong `Domain/Entities/`, DTO trong `Application/DTOs/`
- Mỗi controller kế thừa `BaseApiController` và dùng `ApiResponse<T>`

---

## PHASE 1 — Buổi sáng: Device Validation Error Codes (4 tiếng)

### Task 1.1 — Tạo `DeviceValidationResult` enum

**File cần tạo:** `Domain/Enums/DeviceValidationResult.cs`

```csharp
namespace Ecommerce.Domain.Enums;

public enum DeviceValidationResult
{
    Valid,
    MissingHeader,    // Không có X-Device-Id header
    DeviceMismatch,   // Hash tính từ header != claim dbh trong JWT
    SessionRevoked,   // SessionId không tồn tại trong Redis hoặc DB
    SessionRotated    // dbh trong DB khác với dbh trong JWT
}
```

---

### Task 1.2 — Tạo `DeviceValidationException`

**File cần tạo:** `Application/Exceptions/DeviceValidationException.cs`

```csharp
using Ecommerce.Domain.Enums;

namespace Ecommerce.Application.Exceptions;

public class DeviceValidationException : Exception
{
    public DeviceValidationResult Reason { get; }

    public DeviceValidationException(DeviceValidationResult reason)
        : base($"Device validation failed: {reason}")
    {
        Reason = reason;
    }
}
```

---

### Task 1.3 — Refactor logic validate device trong `AuthService`

**File cần chỉnh sửa:** Tìm file xử lý device validation (thường là `AuthService.cs` hoặc `AuthExtensions.cs` trong `Infrastructure/Services/` hoặc `API/Extensions/`)

**Yêu cầu:**
1. Tìm method hiện tại đang validate `X-Device-Id` header
2. Tách logic thành 4 bước rõ ràng theo thứ tự sau, mỗi bước throw đúng `DeviceValidationException` với đúng `Reason`:

```
Bước 1: Kiểm tra header X-Device-Id có tồn tại không
        → Nếu rỗng hoặc null: throw DeviceValidationException(MissingHeader)

Bước 2: Tính HMAC của header value, so với claim "dbh" trong JWT
        → Nếu không khớp: throw DeviceValidationException(DeviceMismatch)

Bước 3: Lấy sessionId từ claim "sid", tra Redis trước
        → Nếu Redis có giá trị: so với claimHash
            → Không khớp: throw DeviceValidationException(SessionRotated)
        → Nếu Redis không có: fallback xuống DB (bước 4)

Bước 4: Tra DB
        → Nếu không tìm thấy: throw DeviceValidationException(SessionRevoked)
        → Nếu tìm thấy nhưng khác claimHash: throw DeviceValidationException(SessionRotated)
```

**Lưu ý:** Giữ nguyên method signature hiện tại nếu có thể. Chỉ thay các exception đang throw bằng `DeviceValidationException` tương ứng.

---

### Task 1.4 — Cập nhật `GlobalExceptionMiddleware`

**File cần chỉnh sửa:** `Infrastructure/Middleware/GlobalExceptionMiddleware.cs` (hoặc tên tương tự trong project)

Thêm case xử lý `DeviceValidationException` vào switch/if-else đang có:

```csharp
case DeviceValidationException dvEx:
    (statusCode, errorCode, message) = dvEx.Reason switch
    {
        DeviceValidationResult.MissingHeader  => (400, "DEVICE_HEADER_MISSING",  "X-Device-Id header is required"),
        DeviceValidationResult.DeviceMismatch => (401, "DEVICE_MISMATCH",        "Token used from unrecognized device"),
        DeviceValidationResult.SessionRevoked => (401, "SESSION_REVOKED",         "Session has been revoked. Please login again"),
        DeviceValidationResult.SessionRotated => (401, "SESSION_ROTATED",         "Session was replaced. Please re-authenticate"),
        _                                     => (401, "DEVICE_INVALID",          "Device validation failed")
    };
    break;
```

**Đảm bảo:** Response format giữ nguyên `ApiResponse<T>` đang dùng cho các exception khác.

---

### Task 1.5 — Viết Unit Tests cho Device Validation

**File cần tạo hoặc bổ sung:** `Ecommerce.UnitTests/Auth/DeviceValidationTests.cs`

Viết test cho 5 scenario sau (dùng cùng mock/pattern với các test Auth đang có trong project):

```
Test 1: Header rỗng → throw DeviceValidationException với Reason = MissingHeader
Test 2: Header sai (hash không khớp JWT) → Reason = DeviceMismatch  
Test 3: Hash đúng, Redis không có session, DB không có → Reason = SessionRevoked
Test 4: Hash đúng, Redis không có, DB có nhưng khác JWT → Reason = SessionRotated
Test 5: Hash đúng, Redis có và khớp JWT → không throw, return bình thường
```

Sau khi viết xong, chạy: `dotnet test --filter "DeviceValidation"`  
Đảm bảo tất cả 5 test PASS.

---

## PHASE 2 — Buổi chiều: Device Registry (4 tiếng)

### Task 2.1 — Tạo Entity `DeviceSession`

**File cần tạo:** `Domain/Entities/DeviceSession.cs`

```csharp
namespace Ecommerce.Domain.Entities;

public class DeviceSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;  // khớp claim "sid"
    public string DeviceHash { get; set; } = string.Empty; // dbh
    public string DeviceName { get; set; } = string.Empty; // parse từ User-Agent
    public string IpAddress { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;

    // Navigation
    public User User { get; set; } = null!;
}
```

**Thêm vào DbContext:**
1. Tìm file DbContext của project (thường `AppDbContext.cs` hoặc `EcommerceDbContext.cs`)
2. Thêm: `public DbSet<DeviceSession> DeviceSessions { get; set; }`
3. Trong `OnModelCreating`, thêm config:

```csharp
modelBuilder.Entity<DeviceSession>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.SessionId).IsRequired().HasMaxLength(128);
    e.Property(x => x.DeviceHash).IsRequired().HasMaxLength(256);
    e.Property(x => x.DeviceName).HasMaxLength(200);
    e.Property(x => x.IpAddress).HasMaxLength(64);
    e.HasIndex(x => x.SessionId);
    e.HasIndex(x => new { x.UserId, x.IsRevoked });
});
```

**Chạy migration:**
```bash
dotnet ef migrations add AddDeviceSession --project Infrastructure --startup-project API
dotnet ef database update --project Infrastructure --startup-project API
```

> Nếu project structure khác, điều chỉnh `--project` và `--startup-project` cho phù hợp

---

### Task 2.2 — Tạo DTOs

**File cần tạo:** `Application/DTOs/DeviceSessionDto.cs`

```csharp
namespace Ecommerce.Application.DTOs;

public class DeviceSessionDto
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCurrent { get; set; } // true nếu đây là session đang dùng
}
```

---

### Task 2.3 — Tạo Interface `IDeviceSessionService`

**File cần tạo:** `Application/Interfaces/IDeviceSessionService.cs`

```csharp
using Ecommerce.Application.DTOs;

namespace Ecommerce.Application.Interfaces;

public interface IDeviceSessionService
{
    /// <summary>
    /// Đăng ký thiết bị mới sau khi login thành công
    /// </summary>
    Task RegisterAsync(Guid userId, string sessionId, string deviceHash, string userAgent, string ipAddress);

    /// <summary>
    /// Cập nhật LastSeenAt mỗi khi request hợp lệ đến
    /// </summary>
    Task UpdateLastSeenAsync(string sessionId);

    /// <summary>
    /// Lấy danh sách tất cả thiết bị đang active của user
    /// </summary>
    Task<List<DeviceSessionDto>> GetActiveDevicesAsync(Guid userId, string currentSessionId);

    /// <summary>
    /// Revoke một thiết bị cụ thể (chỉ owner mới được revoke)
    /// </summary>
    Task RevokeAsync(Guid userId, Guid deviceSessionId);

    /// <summary>
    /// Revoke tất cả thiết bị của user (trừ session hiện tại nếu keepCurrent = true)
    /// </summary>
    Task RevokeAllAsync(Guid userId, string currentSessionId, bool keepCurrent = false);
}
```

---

### Task 2.4 — Implement `DeviceSessionService`

**File cần tạo:** `Infrastructure/Services/DeviceSessionService.cs`

Implement toàn bộ interface trên với các yêu cầu sau:

**RegisterAsync:**
- Parse `DeviceName` từ `userAgent` theo logic:
  ```
  "PostmanRuntime" → "Postman"
  "swagger"        → "Swagger UI"  
  "Windows"        → "Windows Browser"
  "Macintosh"      → "Mac Browser"
  "iPhone"         → "iPhone"
  "Android"        → "Android Device"
  Còn lại          → "Unknown Device"
  ```
- Nếu đã có DeviceSession với cùng `sessionId` và `IsRevoked = false`, không tạo mới, chỉ update `LastSeenAt`
- Lưu vào DB

**UpdateLastSeenAsync:**
- Tìm DeviceSession theo `sessionId`, update `LastSeenAt = DateTime.UtcNow`
- Nếu không tìm thấy, bỏ qua (không throw)

**GetActiveDevicesAsync:**
- Query các DeviceSession có `UserId = userId` và `IsRevoked = false`
- Map sang `DeviceSessionDto`
- Set `IsCurrent = true` nếu `SessionId == currentSessionId`
- Order by `LastSeenAt descending`

**RevokeAsync:**
- Tìm DeviceSession theo `Id` và `UserId` (bắt buộc kiểm tra UserId để tránh user xóa session người khác)
- Nếu không tìm thấy hoặc `UserId` không khớp: throw `NotFoundException` (dùng exception class đang có trong project)
- Set `IsRevoked = true`, lưu DB
- Xóa cache Redis của session đó nếu có (key: `session:{sessionId}:dbh`)

**RevokeAllAsync:**
- Query tất cả DeviceSession của `userId` có `IsRevoked = false`
- Nếu `keepCurrent = true`: exclude session có `SessionId == currentSessionId`
- Set tất cả `IsRevoked = true`
- Xóa tất cả cache Redis tương ứng
- Lưu DB một lần duy nhất (`SaveChangesAsync` một lần)

**Đăng ký DI:** Tìm file `AddInfrastructure` extension, thêm:
```csharp
services.AddScoped<IDeviceSessionService, DeviceSessionService>();
```

---

### Task 2.5 — Tích hợp `RegisterAsync` vào Login flow

**File cần chỉnh sửa:** Tìm method xử lý Login trong `AuthService.cs`

Sau khi tạo JWT thành công, thêm:
```csharp
await _deviceSessionService.RegisterAsync(
    userId:      user.Id,
    sessionId:   sessionId,   // claim "sid" đã tạo
    deviceHash:  dbh,         // đã tính khi tạo JWT
    userAgent:   httpContext.Request.Headers["User-Agent"].ToString(),
    ipAddress:   httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
);
```

---

### Task 2.6 — Tạo `DeviceController`

**File cần tạo:** `API/Controllers/DeviceController.cs`

```csharp
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[Authorize]
[Route("api/devices")]
public class DeviceController : BaseApiController
{
    private readonly IDeviceSessionService _deviceSessionService;

    public DeviceController(IDeviceSessionService deviceSessionService)
    {
        _deviceSessionService = deviceSessionService;
    }

    /// <summary>
    /// Lấy danh sách thiết bị đang đăng nhập của user hiện tại
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyDevices()
    {
        var userId = GetCurrentUserId();       // dùng helper đang có trong BaseApiController
        var sessionId = GetCurrentSessionId(); // lấy claim "sid"
        var devices = await _deviceSessionService.GetActiveDevicesAsync(userId, sessionId);
        return Ok(ApiResponse<List<DeviceSessionDto>>.Success(devices));
    }

    /// <summary>
    /// Revoke một thiết bị cụ thể
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RevokeDevice(Guid id)
    {
        var userId = GetCurrentUserId();
        await _deviceSessionService.RevokeAsync(userId, id);
        return Ok(ApiResponse<string>.Success("Device revoked successfully"));
    }

    /// <summary>
    /// Logout tất cả thiết bị (trừ thiết bị hiện tại)
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> RevokeAllDevices([FromQuery] bool includeCurrentDevice = false)
    {
        var userId = GetCurrentUserId();
        var sessionId = GetCurrentSessionId();
        await _deviceSessionService.RevokeAllAsync(userId, sessionId, keepCurrent: !includeCurrentDevice);
        return Ok(ApiResponse<string>.Success("All devices revoked successfully"));
    }
}
```

**Lưu ý:** Nếu `BaseApiController` chưa có `GetCurrentSessionId()`, thêm vào:
```csharp
protected string GetCurrentSessionId()
    => User.FindFirst("sid")?.Value ?? string.Empty;
```

---

### Task 2.7 — Verification checklist cuối ngày

Chạy toàn bộ các lệnh sau theo thứ tự, đảm bảo tất cả PASS:

```bash
# 1. Build không lỗi
dotnet build

# 2. Tất cả unit test pass
dotnet test

# 3. Migration đã apply
dotnet ef migrations list --project Infrastructure --startup-project API
```

Sau đó test thủ công trên Swagger theo kịch bản:

```
Kịch bản 1 — Happy path:
  1. POST /api/auth/login với X-Device-Id: "test-device-001"
     → Expect: 200 OK, nhận JWT
  2. GET /api/devices với JWT vừa lấy, header X-Device-Id: "test-device-001"
     → Expect: 200 OK, thấy 1 device "Swagger UI", IsCurrent: true

Kịch bản 2 — DeviceMismatch:
  1. Dùng JWT từ kịch bản 1
  2. Gọi GET /api/devices với X-Device-Id: "wrong-device"
     → Expect: 401, errorCode: "DEVICE_MISMATCH"

Kịch bản 3 — Revoke và SessionRevoked:
  1. POST /api/auth/login lần 2 → nhận JWT_B, ghi nhớ device ID từ GET /api/devices
  2. Dùng JWT_A: DELETE /api/devices/{id của session B}
     → Expect: 200 OK
  3. Dùng JWT_B gọi bất kỳ protected endpoint
     → Expect: 401, errorCode: "SESSION_REVOKED"

Kịch bản 4 — MissingHeader:
  1. Gọi bất kỳ protected endpoint mà KHÔNG có header X-Device-Id
     → Expect: 400, errorCode: "DEVICE_HEADER_MISSING"
```

---

## Ghi chú cho Agent

- Nếu tên class, namespace, hay convention trong project khác với file này, ưu tiên theo convention của project
- Nếu gặp lỗi compile, đọc kỹ error message, tự fix trước khi dừng lại
- Không thêm package NuGet mới nếu không cần thiết — project đã có đủ dependencies
- Sau Task 1.5 và Task 2.7, tóm tắt ngắn những gì đã làm được và điểm nào cần lưu ý
