namespace Ecommerce.Domain.Entities;

public class DeviceSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string DeviceHash { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; }

    public User User { get; set; } = null!;
}
