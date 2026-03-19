Dưới đây là bản mình viết sẵn để bạn **paste đè** vào `docs/Fix_race_condition.md`.

```md
## Nhiệm vụ

Fix race condition trong `IncrementAsync` của `RedisCacheService` theo hướng **atomic** và an toàn production.

Phải xử lý đầy đủ:
- Key chưa tồn tại
- Key đúng format integer
- Key sai format (legacy JSON/string cũ)
- Redis unavailable (timeout/down)

---

## File cần sửa

- `Ecommerce.Infrastructure/Caching/RedisCacheService.cs`

## File liên quan cần đọc/đối chiếu

- `Program.cs` — DI đăng ký Redis + `IConnectionMultiplexer`
- `appsettings.json` / `appsettings.Development.json` — `ConnectionStrings:Redis`, `Redis:InstanceName`, `Redis:Enabled`

---

## Bước 1 — Chuẩn hóa DI Redis trong `Program.cs`

### 1.1 Đăng ký `IConnectionMultiplexer` bằng `ConfigurationOptions`

Yêu cầu:
- Dùng đúng chuỗi kết nối từ `ConnectionStrings:Redis`
- `AbortOnConnectFail = false`
- Không hard-code connection string
- Thống nhất với `AddStackExchangeRedisCache`

Ví dụ hướng triển khai:

```csharp
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
var redisInstanceName = builder.Configuration["Redis:InstanceName"] ?? "EcommerceAPI:";

if (redisEnabled && !string.IsNullOrWhiteSpace(redisConnection))
{
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = false;

    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(options));

    builder.Services.AddStackExchangeRedisCache(opt =>
    {
        opt.Configuration = redisConnection;
        opt.InstanceName = redisInstanceName;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
```

### 1.2 Kiểm tra mode Redis tắt

Phải có quyết định rõ:
- Nếu `Redis:Enabled=false`, `ICacheService` có dùng được không?
- Không để runtime rơi vào trạng thái inject `RedisCacheService` nhưng thiếu `IConnectionMultiplexer`.

---

## Bước 2 — Sửa `IncrementAsync` theo Lua script atomic

## 2.1 Vì sao cần Lua

Pattern `DEL + INCR` tách rời vẫn race condition khi nhiều request cùng self-heal.

Phải đổi sang **1 Lua script** làm toàn bộ:
1. đọc key,
2. normalize legacy value,
3. increment,
4. set TTL nếu cần.

=> Một round-trip, atomic trên Redis server.

## 2.2 Hành vi bắt buộc của script

- **Case A: key chưa tồn tại**  
  `INCR key`, set TTL ngay.
- **Case B: key là integer hợp lệ**  
  `INCR key`, không reset TTL nếu key đã có TTL.
- **Case C: key sai format (ví dụ `"1"` hoặc JSON cũ)**  
  cố parse về số, parse fail thì base = 0, sau đó set lại raw integer rồi `INCR`.
- **Case D: Redis lỗi**  
  exception đi lên caller (không nuốt lỗi ở infrastructure).

## 2.3 TTL rule

- Key mới tạo: set expire ngay (`expiry` hoặc default)
- Key đã tồn tại:
  - nếu chưa có TTL (`TTL == -1`) thì set TTL
  - nếu đã có TTL thì giữ nguyên (không reset mỗi lần increment)

## 2.4 Gợi ý script (tham khảo)

```lua
-- KEYS[1] = full key
-- ARGV[1] = ttl seconds

local key = KEYS[1]
local ttlSeconds = tonumber(ARGV[1])

local current = redis.call('GET', key)
local exists = current ~= false

if not exists then
  local v = redis.call('INCR', key)
  if ttlSeconds and ttlSeconds > 0 then
    redis.call('EXPIRE', key, ttlSeconds)
  end
  return v
end

local num = tonumber(current)
if not num then
  -- try parse legacy quoted numeric string: "123"
  local unquoted = string.match(current, '^"(%-?%d+)"$')
  if unquoted then
    num = tonumber(unquoted)
  end
end

if not num then
  num = 0
end

redis.call('SET', key, tostring(num))
local nextVal = redis.call('INCR', key)

local ttl = redis.call('TTL', key)
if ttl == -1 and ttlSeconds and ttlSeconds > 0 then
  redis.call('EXPIRE', key, ttlSeconds)
end

return nextVal
```

> Ghi chú: Có thể dùng `LuaScript.Prepare(...)` + `ScriptEvaluateAsync(...)` trong `StackExchange.Redis`.

---

## Bước 3 — Fallback hành vi ở Application Services

Vì `IncrementAsync` có thể throw khi Redis lỗi:
- Rà soát các chỗ gọi bump version (`ProductService`, `CategoryService`, `OrderService`, ...)
- Đảm bảo mutation DB chính không fail chỉ vì cache/version bump fail
- Log warning đầy đủ context khi degrade

---

## Bước 4 — Migration key legacy (không gây rollback version)

Không dùng cách xóa key rồi để bắt đầu lại từ 1 trong production.

### 4.1 Mục tiêu migration
- Key version phải **monotonic non-decreasing**
- Không tạo nguy cơ đụng lại version cũ còn tồn tại cache list TTL

### 4.2 Cách làm
- Đọc key hiện tại
- Nếu legacy format, normalize sang integer hợp lệ
- Parse fail: set giá trị an toàn (ví dụ unix timestamp hiện tại) thay vì 1
- Sau migration, toàn bộ bump dùng Lua atomic

---

## Bước 5 — Test plan bắt buộc

- [ ] **Concurrency test**: 100–1000 request song song, giá trị cuối đúng bằng số lần increment
- [ ] **Legacy format test**: key `"1"`/JSON cũ được self-heal và tiếp tục tăng đúng
- [ ] **TTL test**:
  - key mới có TTL
  - key đã có TTL không bị reset mỗi lần bump
  - key không TTL thì được set TTL
- [ ] **Redis failure test**: Redis down/timeout không làm vỡ luồng nghiệp vụ chính (nếu đã thiết kế degrade)

---

## Ràng buộc

- Không thay đổi namespace/class name
- Không tạo breaking change cho `ICacheService` nếu chưa thống nhất toàn bộ caller
- Không sửa logic ngoài phạm vi cần thiết nếu không có lý do rõ ràng

---

## Tiêu chí hoàn thành

- [ ] `IncrementAsync` không còn pattern `DEL + INCR` tách rời
- [ ] Dùng Lua script atomic cho normalize + increment + TTL
- [ ] TTL không bị reset mỗi lần increment
- [ ] Không có rollback/đụng lại version cũ do migration
- [ ] DI Redis ổn định khi startup (kể cả Redis khởi động chậm)
- [ ] Redis lỗi được propagate/fallback đúng thiết kế
- [ ] Test plan ở trên pass
```

Nếu bạn muốn, mình có thể viết thêm một bản **rút gọn 1 trang** để giao task cho teammate (ít chi tiết kỹ thuật hơn, dễ execute).