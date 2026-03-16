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

    public async Task<Role?> GetByIdAsync(Guid id)
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

    public async Task AssignPermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds)
    {
        var existing = _context.RolePermissions.Where(rp => rp.RoleId == roleId);
        _context.RolePermissions.RemoveRange(existing);

        var distinctIds = permissionIds.Distinct().ToList();

        var newLinks = distinctIds.Select(pid => new RolePermission
        {
            RoleId = roleId,
            PermissionId = pid
        });

        await _context.RolePermissions.AddRangeAsync(newLinks);
        await _context.SaveChangesAsync();
    }

    public async Task AssignRoleToUserAsync(int userId, Guid roleId)
    {
        var existing = await _context.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (existing != null)
        {
            return;
        }

        await _context.UserRoles.AddAsync(new UserRole
        {
            UserId = userId,
            RoleId = roleId
        });

        await _context.SaveChangesAsync();
    }
}
