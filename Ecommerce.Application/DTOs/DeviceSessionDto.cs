namespace Ecommerce.Application.DTOs;

public class DeviceSessionDto
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCurrent { get; set; }
}
