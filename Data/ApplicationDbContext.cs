using Microsoft.EntityFrameworkCore;
using Ecommerce.API.Models;

namespace Ecommerce.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
}
