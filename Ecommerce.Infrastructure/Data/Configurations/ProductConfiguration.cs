using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ecommerce.Domain.Entities;

namespace Ecommerce.API.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.ToTable("Products");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(p => p.Price)
                .HasColumnType("decimal(18,2)");

            builder.Property(p => p.CategoryId)
                .IsRequired();

            builder.Property(p => p.CreatedAt)
                .HasDefaultValueSql("now()");

            builder.HasIndex(p => p.Name);

            builder.HasIndex(p => p.CategoryId);

            builder.HasIndex(p => p.Price);

            builder.Property(p => p.Stock)
                .IsRequired()
                .HasDefaultValue(0);

            builder.UseXminAsConcurrencyToken();

            builder.HasIndex(p => new { p.CategoryId, p.Id });

            builder.HasQueryFilter(p => !p.IsDeleted);

            builder.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
