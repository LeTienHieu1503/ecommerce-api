using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Common.Enums;

namespace Ecommerce.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.UserId)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.UseXminAsConcurrencyToken();

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(o => o.StripePaymentIntentId)
            .HasMaxLength(128);

        builder.Property(o => o.PaymentStatus)
            .IsRequired()
            .HasConversion<int>();

        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.StripePaymentIntentId);
    }
}
