using Ecommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Data;

public static class PermissionSeeder
{
    private static readonly string[] Entities = { "product", "category" };
    private static readonly string[] Actions = { "create", "read", "update", "delete" };

    public static async Task SeedPermissionsAsync(ApplicationDbContext dbContext)
    {
        if (await dbContext.Permissions.AnyAsync())
        {
            return;
        }

        var permissions = new List<Permission>();

        foreach (var entity in Entities)
        {
            foreach (var action in Actions)
            {
                var name = $"{entity}.{action}";

                permissions.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Entity = entity,
                    Action = action
                });
            }
        }

        await dbContext.Permissions.AddRangeAsync(permissions);
        await dbContext.SaveChangesAsync();
    }
}
