# Dynamic Role & Permission RBAC Implementation

## Goal

Implement a dynamic Role-Based Access Control (RBAC) system with
permissions.

Admin can: - Create roles - Assign permissions to roles - Assign roles
to users

Permissions are system-defined and follow this format:

entity.action

Example: product.create product.read product.update product.delete

category.create category.read category.update category.delete

Authorization must be enforced using ASP.NET Core Authorization Policy.

------------------------------------------------------------------------

# Architecture Constraints

The project uses Clean Architecture:

API Application Domain Infrastructure

Responsibilities:

  Layer            Responsibility
  ---------------- ------------------------------------
  Domain           Entities and repository interfaces
  Application      Services and business logic
  Infrastructure   EF Core, repositories, Redis
  API              Controllers, Authorization

------------------------------------------------------------------------

# 1. Domain Layer

Create the following entities.

## Role

Role - Id (Guid) - Name (string) - CreatedAt

## Permission

Permission - Id (Guid) - Name (string) // product.create - Entity
(string) // product - Action (string) // create

## UserRole

UserRole - UserId - RoleId

Many-to-many relationship between User and Role.

## RolePermission

RolePermission - RoleId - PermissionId

Many-to-many relationship between Role and Permission.

------------------------------------------------------------------------

# 2. Repository Interfaces (Domain)

Add interfaces:

IRoleRepository IPermissionRepository

Example methods:

## IRoleRepository

Task\<Role?\> GetByIdAsync(Guid id); Task\<List`<Role>`{=html}\>
GetAllAsync(); Task AddAsync(Role role); Task UpdateAsync(Role role);
Task DeleteAsync(Role role);

## IPermissionRepository

Task\<List`<Permission>`{=html}\> GetAllAsync();
Task\<List`<Permission>`{=html}\> GetByRoleIdAsync(Guid roleId);

------------------------------------------------------------------------

# 3. Infrastructure Layer

Implement repositories using Entity Framework Core.

Create:

RoleRepository PermissionRepository

Update ApplicationDbContext.

Add DbSets:

DbSet`<Role>`{=html} DbSet`<Permission>`{=html}
DbSet`<RolePermission>`{=html} DbSet`<UserRole>`{=html}

------------------------------------------------------------------------

# 4. Permission Seeder

Permissions are system-defined.

Create: Infrastructure/Data/PermissionSeeder.cs

Entities: product category

Actions: create read update delete

Generate permissions automatically:

product.create product.read product.update product.delete

category.create category.read category.update category.delete

Seed permissions during application startup.

------------------------------------------------------------------------

# 5. Application Layer

Create services.

Interfaces: IRoleService IPermissionService

## RoleService responsibilities

CreateRoleAsync() AssignPermissionsAsync(roleId, permissionIds)
AssignRoleToUserAsync(userId, roleId)

## PermissionService responsibilities

GetAllPermissionsAsync() GetUserPermissionsAsync(userId)

Return type: List`<string>`{=html}

Example: \["product.create","product.read"\]

------------------------------------------------------------------------

# 6. Redis Permission Cache

Cache permissions per user.

Redis key: permissions:{userId}

Value: \["product.create","product.read"\]

Flow: Request -\> AuthorizationHandler -\> Redis -\> DB if miss -\>
Cache

Invalidate cache when: - role permission changes - user role changes

------------------------------------------------------------------------

# 7. Authorization Layer

Inside API project:

Authorization/ Requirements/ Handlers/

## PermissionRequirement

PermissionRequirement - string Permission

Example: product.create

## PermissionHandler

Responsibilities: 1. Extract userId from JWT 2. Load permissions via
IPermissionService 3. Check permission

Logic: if userPermissions contains requiredPermission -\> succeed else
-\> fail

------------------------------------------------------------------------

# 8. Authorization Policy

Controllers will use:

\[Authorize(Policy = "product.create")\]

Example:

POST /products -\> product.create GET /products -\> product.read PUT
/products/{id} -\> product.update DELETE /products/{id} -\>
product.delete

------------------------------------------------------------------------

# 9. Admin APIs

Create controller: RolesController

## Create Role

POST /roles

Body: { "name": "Manager" }

## Get Roles

GET /roles

## Assign Permissions to Role

POST /roles/{roleId}/permissions

Body: { "permissionIds": \["guid","guid"\] }

## Assign Role to User

POST /users/{userId}/roles

Body: { "roleId": "guid" }

------------------------------------------------------------------------

# 10. Permission API (for UI)

Endpoint: GET /permissions

Example response: \[ { "id":"guid", "name":"product.create",
"entity":"product", "action":"create" }\]

Used for combobox in admin UI.

------------------------------------------------------------------------

# 11. Controller Usage Example

\[Authorize(Policy="product.create")\] CreateProduct()

\[Authorize(Policy="product.read")\] GetProducts()

\[Authorize(Policy="product.update")\] UpdateProduct()

\[Authorize(Policy="product.delete")\] DeleteProduct()

------------------------------------------------------------------------

# 12. Testing

Add unit tests for: RoleService PermissionService PermissionHandler

Test scenarios: - user with permission -\> authorized - user without
permission -\> forbidden - redis cache hit - redis cache miss

------------------------------------------------------------------------

# Expected Result

The system must support:

-   dynamic roles
-   system-defined permissions
-   role-permission mapping
-   user-role mapping
-   policy-based authorization
-   redis permission caching
