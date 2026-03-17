namespace Ecommerce.Domain.Entities
{
    public class Permission
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Entity { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}
