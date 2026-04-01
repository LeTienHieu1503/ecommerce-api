using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Application.Common.Http;

public static class RequestDeviceDiagnostics
{
    public static void Log(
        ILogger logger,
        IRequestDeviceContext requestDevice,
        string componentName,
        string operationName)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        logger.LogDebug(
            "{Component}.{Operation} | DeviceBound={DeviceBound} | NormalizedDeviceId={DeviceId}",
            componentName,
            operationName,
            requestDevice.IsDeviceBound,
            requestDevice.NormalizedDeviceId);
    }
}
