using System.Security.Cryptography;
using System.Text;

namespace Ecommerce.Application.Common.Security;

public static class DeviceBindingHelper
{
    private const int MaxDeviceIdLength = 256;

    public static string ComputeDeviceHash(string deviceId, string secret)
    {
        var normalized = NormalizeDeviceId(deviceId);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    public static string NormalizeDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return "unknown";

        var trimmed = deviceId.Trim().ToLowerInvariant();
        return trimmed.Length > MaxDeviceIdLength
            ? trimmed[..MaxDeviceIdLength]
            : trimmed;
    }
}
