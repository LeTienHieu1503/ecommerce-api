using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Domain.Entities;

namespace Ecommerce.Application.Interfaces;

public interface IDeviceBindingValidationService
{
    Task ValidateAsync(
        string? deviceIdHeader,
        string? jwtDbhClaim,
        bool sessionLoadedFromRedis,
        UserSessionState sessionState,
        User user,
        CancellationToken cancellationToken = default);
}
