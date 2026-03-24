using Ecommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Data;

public static class AdminSeeder
{
    /// <summary>
    /// Must stay lowercase: <see cref="Ecommerce.Application.Services.AuthService.LoginAsync"/> normalizes email with ToLower().
    /// </summary>
    private const string AdminEmail = "admin";

    /// <summary>Legacy seed used mixed case; existing DBs would get 401 on login until normalized.</summary>
    private const string LegacyAdminEmail = "Admin";

    public static async Task SeedAdminAsync(ApplicationDbContext context)
    {
        var userRole = await context.Roles
            .FirstOrDefaultAsync(r => r.Name == "User");

        if (userRole == null)
        {
            userRole = new Role
            {
                Name = "User"
            };

            context.Roles.Add(userRole);
            await context.SaveChangesAsync();
        }

        var adminRole = await context.Roles
            .FirstOrDefaultAsync(r => r.Name == "Admin");

        if (adminRole == null)
        {
            adminRole = new Role
            {
                Name = "Admin"
            };

            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
        }

        var legacyAdmin = await context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == LegacyAdminEmail);

        if (legacyAdmin != null)
        {
            legacyAdmin.Email = AdminEmail;
            await context.SaveChangesAsync();
        }

        var adminUser = await context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == AdminEmail);

        if (adminUser != null)
            return;

        var user = new User
        {
            Email = AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456")
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = adminRole.Id
        });

        await context.SaveChangesAsync();
    }
}
