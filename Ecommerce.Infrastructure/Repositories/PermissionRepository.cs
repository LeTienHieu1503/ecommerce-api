using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Repositories;

public class PermissionRepository : IPermissionRepository
{
    private readonly ApplicationDbContext _context;

    public PermissionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Permission>> GetAllAsync()
    {
        return await _context.Permissions
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<Permission>> GetByRoleIdAsync(int roleId)
    {
        return await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<string>> GetPermissionsByUserIdAsync(int userId)
    {
        return await _context.UserRoles
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToListAsync();
    }

    public async Task<Permission?> GetByIdAsync(int id)
    {
        return await _context.Permissions.FindAsync(id);
    }

    public async Task AddAsync(Permission permission)
    {
        await _context.Permissions.AddAsync(permission);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Permission permission)
    {
        _context.Permissions.Update(permission);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Permission permission)
    {
        _context.Permissions.Remove(permission);
        await _context.SaveChangesAsync();
    }
}
