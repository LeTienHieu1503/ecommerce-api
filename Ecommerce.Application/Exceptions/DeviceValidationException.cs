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
