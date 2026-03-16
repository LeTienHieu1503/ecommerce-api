# AI Agent Execution Plan

## Dynamic Permission RBAC Implementation

This document provides a **step-by-step implementation plan** for an AI
coding agent to implement the Dynamic Role & Permission RBAC feature in
the existing Clean Architecture project.

The goal is to ensure the AI: - follows the existing architecture - does
not break project structure - implements each layer in the correct order

Project layers:

API\
Application\
Domain\
Infrastructure

------------------------------------------------------------------------

# Implementation Steps

## Step 1 --- Create Domain Entities

Create entities inside:

Domain/Entities

Add:

Role.cs Permission.cs UserRole.cs RolePermission.cs

### Role

Fields: Id (Guid)\
Name (string)\
CreatedAt (DateTime)

### Permission

Fields: Id (Guid)\
Name (string)\
Entity (string)\
Action (string)

### UserRole

Fields: UserId (Guid)\
RoleId (Guid)

### RolePermission

Fields: RoleId (Guid)\
PermissionId (Guid)

Configure navigation properties where appropriate.

------------------------------------------------------------------------

## Step 2 --- Add Repository Interfaces

Inside:

Domain/Interfaces

Create:

IRoleRepository.cs\
IPermissionRepository.cs

### IRoleRepository

Methods:

GetByIdAsync\
GetAllAsync\
AddAsync\
UpdateAsync\
DeleteAsync

### IPermissionRepository

Methods:

GetAllAsync\
GetByRoleIdAsync\
GetPermissionsByUserIdAsync

------------------------------------------------------------------------

## Step 3 --- Update DbContext

Inside:

Infrastructure/Data/ApplicationDbContext.cs

Add DbSets:

DbSet`<Role>`{=html}\
DbSet`<Permission>`{=html}\
DbSet`<UserRole>`{=html}\
DbSet`<RolePermission>`{=html}

Configure relationships using Fluent API.

Relationships:

User many-to-many Role\
Role many-to-many Permission

------------------------------------------------------------------------

## Step 4 --- Create Entity Configurations

Inside:

Infrastructure/Data/Configurations

Add:

RoleConfiguration.cs\
PermissionConfiguration.cs\
UserRoleConfiguration.cs\
RolePermissionConfiguration.cs

Configure:

-   composite keys
-   relationships
-   indexes

------------------------------------------------------------------------

## Step 5 --- Create Repositories

Inside:

Infrastructure/Repositories

Create:

RoleRepository.cs\
PermissionRepository.cs

Implement the repository interfaces using EF Core.

------------------------------------------------------------------------

## Step 6 --- Create Permission Seeder

Inside:

Infrastructure/Data

Create:

PermissionSeeder.cs

Define:

Entities: product category

Actions: create read update delete

Generate permissions:

product.create\
product.read\
product.update\
product.delete

category.create\
category.read\
category.update\
category.delete

Seed during application startup.

------------------------------------------------------------------------

## Step 7 --- Create Application Interfaces

Inside:

Application/Interfaces

Add:

IRoleService.cs\
IPermissionService.cs

Responsibilities:

RoleService: - create roles - assign permissions to roles - assign roles
to users

PermissionService: - get all permissions - get permissions for a user

------------------------------------------------------------------------

## Step 8 --- Implement Application Services

Inside:

Application/Services

Create:

RoleService.cs\
PermissionService.cs

PermissionService must:

1.  load user roles
2.  load role permissions
3.  return permission list

Return type:

List`<string>`{=html}

Example:

product.create\
product.read

------------------------------------------------------------------------

## Step 9 --- Add Redis Permission Cache

Inside:

Infrastructure/Caching

Extend existing Redis service or create helper methods.

Cache key:

permissions:{userId}

Value example:

\["product.create","product.read"\]

Flow:

Check cache\
If miss → load from DB\
Store result in Redis

Invalidate cache when:

-   role permission changes
-   user role changes

------------------------------------------------------------------------

## Step 10 --- Create Authorization Requirement

Inside:

API/Authorization/Requirements

Create:

PermissionRequirement.cs

Fields:

Permission (string)

------------------------------------------------------------------------

## Step 11 --- Create Authorization Handler

Inside:

API/Authorization/Handlers

Create:

PermissionHandler.cs

Responsibilities:

1.  extract userId from JWT
2.  load permissions via PermissionService
3.  verify permission exists

If permission exists → succeed

------------------------------------------------------------------------

## Step 12 --- Register Authorization Handler

Inside:

Program.cs

Register:

AuthorizationHandler\
PermissionService\
RoleService

------------------------------------------------------------------------

## Step 13 --- Configure Authorization Policies

Policies will be used dynamically.

Controllers will call:

\[Authorize(Policy = "product.create")\]

Ensure AuthorizationHandler can evaluate these policies.

------------------------------------------------------------------------

## Step 14 --- Create RolesController

Inside:

API/Controllers

Add endpoints:

POST /roles\
GET /roles\
POST /roles/{roleId}/permissions

------------------------------------------------------------------------

## Step 15 --- Create PermissionController

Endpoint:

GET /permissions

Purpose:

Return permission list for admin UI combobox.

------------------------------------------------------------------------

## Step 16 --- Add Assign Role Endpoint

Inside existing user controller or new endpoint:

POST /users/{userId}/roles

Body:

roleId

------------------------------------------------------------------------

## Step 17 --- Protect Existing Controllers

Example ProductController:

POST → product.create\
GET → product.read\
PUT → product.update\
DELETE → product.delete

Use:

\[Authorize(Policy = "product.create")\]

------------------------------------------------------------------------

## Step 18 --- Update Dependency Injection

Register services and repositories inside:

Program.cs

or extension methods.

------------------------------------------------------------------------

## Step 19 --- Create Unit Tests

Inside test project.

Add tests for:

RoleService\
PermissionService\
PermissionHandler

Test scenarios:

user has permission → authorized\
user missing permission → forbidden

------------------------------------------------------------------------

## Step 20 --- Verify System Flow

Final authorization flow:

User login → JWT issued

Request → Authorization middleware

Handler loads permissions

Permissions checked against policy

Access granted or denied.

------------------------------------------------------------------------

# Expected Final Result

System supports:

dynamic roles\
system-defined permissions\
role-permission mapping\
user-role mapping\
policy-based authorization\
redis permission caching

Architecture remains compliant with Clean Architecture.
