using Ecommerce.Application.DTOs;

namespace Ecommerce.Application.Interfaces;

public interface IDeviceSessionService
{
    Task RegisterAsync(
        int userId,
        string sessionId,
        string deviceHash,
        string userAgent,
        string ipAddress,
        CancellationToken cancellationToken = default);

    Task UpdateLastSeenAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<List<DeviceSessionDto>> GetActiveDevicesAsync(
        int userId,
        string currentSessionId,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(int userId, Guid deviceSessionId, CancellationToken cancellationToken = default);

    Task RevokeAllAsync(
        int userId,
        string currentSessionId,
        bool keepCurrent = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns false if a registry row exists for the session and is revoked; true if no row (legacy) or active.
    /// </summary>
    Task<bool> IsSessionActiveAsync(string sessionId, CancellationToken cancellationToken = default);
}
