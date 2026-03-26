# Kế hoạch: Ràng buộc phiên theo thiết bị + IP

## Mục tiêu

- Mỗi lần đăng nhập, server lưu **hash mã nhận dạng trình duyệt** (`DeviceId` do client web tạo + lưu trong `localStorage`) vào phiên (`UserSessionState` + cột `LastLoginDeviceHash` trên `User`).
- Access token mang claim `dbh` (device binding hash), tương tự `iph`.
- Mỗi request API: client gửi `X-Device-Id` header; server tính lại hash và so với phiên; không khớp → 401.
- **Backward-compatible**: nếu client không gửi `X-Device-Id` (client cũ / Swagger test), bỏ qua lớp device check (không chặn) — có thể bật bắt buộc sau.

## Lý do bỏ AppVersion

`AppVersion` trên web thay đổi liên tục sau mỗi deploy và không xác định thiết bị. Bỏ để tránh tự làm session vô hiệu sau mỗi lần release.

## Nguyên tắc thiết kế

1. **IP**: Chỉ lấy từ `GetClientIp()` phía server (không tin client tự gửi) — giữ nguyên logic hiện tại.
2. **DeviceId**: UUID do client web tạo bằng `crypto.randomUUID()`, lưu vào `localStorage`. Là *tín hiệu nhận dạng trình duyệt*, không phải bí mật tuyệt đối.
3. **Hash**: `HMACSHA256(DeviceBindingSecret, deviceId.trim().toLower())` — tính phía server, không lưu raw deviceId.
4. **Tách biệt**: kiểm tra `iph` và `dbh` độc lập để đổi mạng/WiFi không làm fail session khi deviceId vẫn khớp.
5. **Backward-compatible**: thiếu header `X-Device-Id` → bỏ qua check `dbh`; `LastLoginDeviceHash` null → bỏ qua.

## Các thay đổi code

### 1. Cấu hình (`appsettings.json`)
Thêm `AuthSecurity:DeviceBindingSecret`.

### 2. Helper (`DeviceBindingHelper.cs`)
Giống `IpBindingHelper`: `ComputeDeviceHash(deviceId, secret)`.

### 3. Domain — `User.cs`
Thêm cột `LastLoginDeviceHash` (nullable string).

### 4. DTO — `UserSessionState.cs`
Thêm `DeviceBindingHash` (nullable string).

### 5. DTO — `LoginRequestDto.cs`
Thêm `DeviceId` (nullable string, không bắt buộc).

### 6. `IJwtTokenService` + `JwtTokenService`
Thêm overload `GenerateToken(..., string? deviceHash)`, claim `dbh`.

### 7. `AuthService`
- `LoginAsync`: tính `DeviceBindingHash` nếu `DeviceId` có, gán vào user + cache.
- `RefreshTokenAsync`: đọc `X-Device-Id` → tính hash → so với session + claim `dbh`.
- `LogoutAsync`: clear `LastLoginDeviceHash`.

### 8. `AuthController`
- `Refresh`: đọc `X-Device-Id` header, truyền xuống `RefreshTokenAsync`.

### 9. `AuthExtensions.OnTokenValidated`
- Đọc `X-Device-Id` header → tính hash → so với `sessionState.DeviceBindingHash` và claim `dbh`.
- Nếu thiếu header hoặc session không có hash → bỏ qua (backward-compatible).

### 10. EF Migration
`AddDeviceHashToUsers`: thêm cột `LastLoginDeviceHash` (nullable).

### 11. Unit tests
Cập nhật `AuthServiceTests`: login với deviceId, mismatch device, backward-compatible (không có deviceId).

## Thứ tự thực thi

1. Config + Helper
2. Entity + DTO
3. JWT service
4. AuthService
5. AuthController
6. AuthExtensions (middleware)
7. Migration
8. Unit tests

## Tài liệu tham chiếu

- `Extensions/AuthExtensions.cs`, `Ecommerce.Application/Services/AuthService.cs`, `JwtTokenService.cs`, `UserSessionState.cs`, `User.cs`, `IpBindingHelper.cs`
