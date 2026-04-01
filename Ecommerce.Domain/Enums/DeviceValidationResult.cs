namespace Ecommerce.Domain.Enums;

public enum DeviceValidationResult
{
    Valid,
    MissingHeader,
    DeviceMismatch,
    SessionRevoked,
    SessionRotated
}
