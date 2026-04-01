using Ecommerce.Application.DTOs;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Services;

public class DeviceSessionService : IDeviceSessionService
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheService _cache;

    public DeviceSessionService(ApplicationDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task RegisterAsync(
        int userId,
        string sessionId,
        string deviceHash,
        string userAgent,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(deviceHash))
            return;

        var deviceName = ParseDeviceName(userAgent);
        var existing = await _db.DeviceSessions
            .FirstOrDefaultAsync(
                x => x.SessionId == sessionId && !x.IsRevoked,
                cancellationToken);

        if (existing != null)
        {
            existing.LastSeenAt = DateTime.UtcNow;
            existing.DeviceHash = deviceHash;
            existing.DeviceName = deviceName;
            existing.IpAddress = ipAddress;
        }
        else
        {
            _db.DeviceSessions.Add(new DeviceSession
            {
                UserId = userId,
                SessionId = sessionId,
                DeviceHash = deviceHash,
                DeviceName = deviceName,
                IpAddress = ipAddress
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _cache.SetAsync(
                SessionDeviceCacheKey(sessionId),
                deviceHash,
                TimeSpan.FromDays(7));
        }
        catch
        {
            // cache optional
        }
    }

    public async Task UpdateLastSeenAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var row = await _db.DeviceSessions
            .FirstOrDefaultAsync(x => x.SessionId == sessionId && !x.IsRevoked, cancellationToken);
        if (row == null)
            return;

        row.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<DeviceSessionDto>> GetActiveDevicesAsync(
        int userId,
        string currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var list = await _db.DeviceSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsRevoked)
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new DeviceSessionDto
            {
                Id = x.Id,
                DeviceName = x.DeviceName,
                IpAddress = x.IpAddress,
                LastSeenAt = x.LastSeenAt,
                CreatedAt = x.CreatedAt,
                IsCurrent = x.SessionId == currentSessionId
            })
            .ToListAsync(cancellationToken);

        return list;
    }

    public async Task RevokeAsync(int userId, Guid deviceSessionId, CancellationToken cancellationToken = default)
    {
        var row = await _db.DeviceSessions
            .FirstOrDefaultAsync(x => x.Id == deviceSessionId, cancellationToken);
        if (row == null || row.UserId != userId)
            throw new NotFoundException("Device session not found");

        row.IsRevoked = true;
        await _db.SaveChangesAsync(cancellationToken);
        await RemoveSessionCacheAsync(row.SessionId);
    }

    public async Task RevokeAllAsync(
        int userId,
        string currentSessionId,
        bool keepCurrent = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.DeviceSessions.Where(x => x.UserId == userId && !x.IsRevoked);
        if (keepCurrent && !string.IsNullOrWhiteSpace(currentSessionId))
            query = query.Where(x => x.SessionId != currentSessionId);

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsRevoked = true;
            await RemoveSessionCacheAsync(row.SessionId);
        }

        if (rows.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsSessionActiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return true;

        var row = await _db.DeviceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
        if (row == null)
            return true;

        return !row.IsRevoked;
    }

    private static string SessionDeviceCacheKey(string sessionId) => $"session:{sessionId}:dbh";

    private async Task RemoveSessionCacheAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;
        try
        {
            await _cache.RemoveAsync(SessionDeviceCacheKey(sessionId));
        }
        catch
        {
            // ignore
        }
    }

    internal static string ParseDeviceName(string userAgent)
    {
        var ua = userAgent ?? string.Empty;
        if (ua.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase))
            return "Postman";
        if (ua.Contains("swagger", StringComparison.OrdinalIgnoreCase))
            return "Swagger UI";
        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            return "Windows Browser";
        if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
            return "Mac Browser";
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            return "iPhone";
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "Android Device";
        return "Unknown Device";
    }
}
