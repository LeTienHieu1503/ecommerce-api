namespace Ecommerce.Application.Interfaces;

public interface IRequestDeviceContext
{
    bool IsDeviceBound { get; }

    string? NormalizedDeviceId { get; }

    int? UserId { get; }
}
