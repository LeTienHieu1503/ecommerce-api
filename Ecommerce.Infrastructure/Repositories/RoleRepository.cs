using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly ApplicationDbContext _context;

    public RoleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Role?> GetByIdAsync(int id)
    {
        return await _context.Roles
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<List<Role>> GetAllAsync()
    {
        return await _context.Roles
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<Role>> GetAllWithPermissionsAsync()
    {
        return await _context.Roles
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .AsNoTracking()
            .ToListAsync();
    }
    public async Task AddAsync(Role role)
    {
        await _context.Roles.AddAsync(role);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Role role)
    {
        _context.Roles.Update(role);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Role role)
    {
        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();
    }

    public async Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds)
    {
        var distinctIds = permissionIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return;

        var existingPermissionIds = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var toAdd = distinctIds
            .Except(existingPermissionIds)
            .Select(pid => new RolePermission
            {
                RoleId = roleId,
                PermissionId = pid
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            await _context.RolePermissions.AddRangeAsync(toAdd);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemovePermissionsAsync(int roleId, IEnumerable<int> permissionIds)
    {
        var distinctIds = permissionIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return;

        var toRemove = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId && distinctIds.Contains(rp.PermissionId))
            .ToListAsync();

        if (toRemove.Count == 0)
            return;

        _context.RolePermissions.RemoveRange(toRemove);
        await _context.SaveChangesAsync();
    }

    public async Task AssignRoleToUserAsync(int userId, int roleId)
    {
        // Replace any existing roles: keep only the new role
        var existingRoles = _context.UserRoles.Where(ur => ur.UserId == userId);
        _context.UserRoles.RemoveRange(existingRoles);

        await _context.UserRoles.AddAsync(new UserRole
        {
            UserId = userId,
            RoleId = roleId
        });

        await _context.SaveChangesAsync();
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        return await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == name);
    }

    public async Task<List<int>> GetUserIdsByRoleIdAsync(int roleId)
    {
        return await _context.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();
    }
}
