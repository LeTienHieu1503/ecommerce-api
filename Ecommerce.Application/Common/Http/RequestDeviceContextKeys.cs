namespace Ecommerce.Application.Common.Http;

/// <summary>
/// Keys for <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> set during JWT validation.
/// </summary>
public static class RequestDeviceContextKeys
{
    public const string IsDeviceBound = "RequestDevice:IsDeviceBound";
    public const string NormalizedDeviceId = "RequestDevice:NormalizedDeviceId";
}
