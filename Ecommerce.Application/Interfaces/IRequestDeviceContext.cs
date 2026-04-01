namespace Ecommerce.Application.Interfaces;

/// <summary>
/// Request-scoped device context populated after JWT + device validation succeeds.
/// Use for audit/logging; security enforcement remains in <see cref="IDeviceBindingValidationService"/>.
/// </summary>
public interface IRequestDeviceContext
{
    /// <summary>
    /// True when the current token/session is device-bound (claim <c>dbh</c> or session device hash).
    /// </summary>
    bool IsDeviceBound { get; }

    /// <summary>
    /// Normalized <c>X-Device-Id</c> when <see cref="IsDeviceBound"/> is true; otherwise null.
    /// </summary>
    string? NormalizedDeviceId { get; }
}
