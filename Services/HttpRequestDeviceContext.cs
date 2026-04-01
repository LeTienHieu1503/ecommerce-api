using Ecommerce.Application.Common.Http;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Ecommerce.API.Services;

public sealed class HttpRequestDeviceContext : IRequestDeviceContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestDeviceContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsDeviceBound =>
        _httpContextAccessor.HttpContext?.Items.TryGetValue(RequestDeviceContextKeys.IsDeviceBound, out var v) == true
        && v is bool b && b;

    public string? NormalizedDeviceId =>
        _httpContextAccessor.HttpContext?.Items.TryGetValue(RequestDeviceContextKeys.NormalizedDeviceId, out var v) == true
            ? v as string
            : null;
}
