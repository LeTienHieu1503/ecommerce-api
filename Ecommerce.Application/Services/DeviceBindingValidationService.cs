using Ecommerce.Application.Common.Security;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Common.Enums;
using Microsoft.Extensions.Configuration;

namespace Ecommerce.Application.Services;

public class DeviceBindingValidationService : IDeviceBindingValidationService
{
    private readonly IConfiguration _configuration;
    private readonly IDeviceSessionService _deviceSessionService;

    public DeviceBindingValidationService(
        IConfiguration configuration,
        IDeviceSessionService deviceSessionService)
    {
        _configuration = configuration;
        _deviceSessionService = deviceSessionService;
    }

    public async Task ValidateAsync(
        string? deviceIdHeader,
        string? jwtDbhClaim,
        bool sessionLoadedFromRedis,
        UserSessionState sessionState,
        User user,
        CancellationToken cancellationToken = default)
    {
        var requiresDeviceBinding =
            !string.IsNullOrWhiteSpace(jwtDbhClaim) ||
            !string.IsNullOrWhiteSpace(sessionState.DeviceBindingHash);

        if (requiresDeviceBinding)
        {
            var secret = _configuration["AuthSecurity:DeviceBindingSecret"] ?? "fallback-device-secret";

            if (string.IsNullOrWhiteSpace(deviceIdHeader))
                throw new DeviceValidationException(DeviceValidationResult.MissingHeader);

            var currentHash = DeviceBindingHelper.ComputeDeviceHash(deviceIdHeader, secret);
            if (string.IsNullOrWhiteSpace(jwtDbhClaim) ||
                !string.Equals(currentHash, jwtDbhClaim, StringComparison.Ordinal))
                throw new DeviceValidationException(DeviceValidationResult.DeviceMismatch);

            if (sessionLoadedFromRedis)
            {
                if (!string.Equals(sessionState.DeviceBindingHash, jwtDbhClaim, StringComparison.Ordinal))
                    throw new DeviceValidationException(DeviceValidationResult.SessionRotated);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(user.LastLoginDeviceHash) &&
                    !string.IsNullOrWhiteSpace(jwtDbhClaim))
                    throw new DeviceValidationException(DeviceValidationResult.SessionRevoked);

                if (!string.Equals(user.LastLoginDeviceHash, jwtDbhClaim, StringComparison.Ordinal))
                    throw new DeviceValidationException(DeviceValidationResult.SessionRotated);
            }
        }

        if (!await _deviceSessionService.IsSessionActiveAsync(sessionState.SessionId, cancellationToken))
            throw new DeviceValidationException(DeviceValidationResult.SessionRevoked);
    }
}
