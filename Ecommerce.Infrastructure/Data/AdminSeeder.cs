using Ecommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Data;

public static class AdminSeeder
{
    public static async Task SeedAdminAsync(ApplicationDbContext context)
    {
        var adminExists = await context.Users
            .AnyAsync(x => x.Role == "Admin");

        if (adminExists)
            return;

        var admin = new User
        {
            Email = "Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            Role = "Admin"
        };

        context.Users.Add(admin);

        await context.SaveChangesAsync();
    }
}
