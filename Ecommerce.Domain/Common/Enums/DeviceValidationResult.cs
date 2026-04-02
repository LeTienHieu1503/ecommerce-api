namespace Ecommerce.Domain.Common.Enums;

public enum DeviceValidationResult
{
    Valid,
    MissingHeader,
    DeviceMismatch,
    SessionRevoked,
    SessionRotated
}
