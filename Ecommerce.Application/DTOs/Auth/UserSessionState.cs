namespace Ecommerce.Application.DTOs.Auth;

public class UserSessionState
{
    public string SessionId { get; set; } = null!;
    public long SessionVersion { get; set; }
    public string IpHash { get; set; } = null!;
    public string? DeviceBindingHash { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}