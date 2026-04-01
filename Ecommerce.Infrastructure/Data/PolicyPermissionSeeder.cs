using Ecommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Data;

/// <summary>Ensures permission rows exist for every JWT policy name (matches AuthExtensions permission policies).</summary>
public static class PolicyPermissionSeeder
{
    private static readonly string[] PolicyPermissionNames =
    {
        "order.create", "order.read", "order.update", "order.delete",
        "order.checkout", "order.refund"
    };

    public static async Task EnsurePolicyPermissionsAsync(ApplicationDbContext dbContext)
    {
        var existing = await dbContext.Permissions
            .Select(p => p.Name)
            .ToListAsync();

        var toAdd = new List<Permission>();
        foreach (var name in PolicyPermissionNames)
        {
            if (existing.Contains(name))
                continue;

            var dot = name.IndexOf('.', StringComparison.Ordinal);
            toAdd.Add(new Permission
            {
                Name = name,
                Entity = name[..dot],
                Action = name[(dot + 1)..]
            });
        }

        if (toAdd.Count == 0)
            return;

        await dbContext.Permissions.AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();
    }
}
