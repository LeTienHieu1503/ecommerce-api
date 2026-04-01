using System.Reflection;
using Ecommerce.API.Responses;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[Authorize]
[Route("api/devices")]
[Authorize(Policy = Authorization.Policies.AuthorizationPolicies.AdminOnly)]
public class DeviceController : BaseApiController
{
    private readonly IDeviceSessionService _deviceSessionService;

    public DeviceController(IDeviceSessionService deviceSessionService)
    {
        _deviceSessionService = deviceSessionService;
    }
    [HttpGet]
    public async Task<IActionResult> GetMyDevices(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        var sessionId = GetCurrentSessionId();
        var devices = await _deviceSessionService.GetActiveDevicesAsync(userId.Value, sessionId, cancellationToken);
        return Success(devices, "OK");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RevokeDevice(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        await _deviceSessionService.RevokeAsync(userId.Value, id, cancellationToken);
        return Success("Device revoked successfully", "Device revoked successfully");
    }

    [HttpDelete]
    public async Task<IActionResult> RevokeAllDevices(
        [FromQuery] bool includeCurrentDevice = false,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        var sessionId = GetCurrentSessionId();
        await _deviceSessionService.RevokeAllAsync(
            userId.Value,
            sessionId,
            keepCurrent: !includeCurrentDevice,
            cancellationToken);
        return Success("All devices revoked successfully", "All devices revoked successfully");
    }
}
