using Ecommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ecommerce.Infrastructure.Data.Configurations;

public class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.SessionId).IsRequired().HasMaxLength(128);
        e.Property(x => x.DeviceHash).IsRequired().HasMaxLength(256);
        e.Property(x => x.DeviceName).HasMaxLength(200);
        e.Property(x => x.IpAddress).HasMaxLength(64);
        e.HasIndex(x => x.SessionId);
        e.HasIndex(x => new { x.UserId, x.IsRevoked });
        e.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
