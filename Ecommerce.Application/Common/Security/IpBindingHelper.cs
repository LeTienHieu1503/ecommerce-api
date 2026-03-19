using System.Security.Cryptography;
using System.Text;

namespace Ecommerce.Application.Common.Security;

public static class IpBindingHelper
{
    public static string ComputeIpHash(string ip, string secret)
    {
        var normalized = NormalizeIp(ip);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    public static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "unknown";

        var first = ip.Split(',')[0].Trim();
        return first;
    }
}