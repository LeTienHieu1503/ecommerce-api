using Ecommerce.Application.DTOs.User;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Moq;

public class RoleServiceTests
{
    // =============================================
    // Dependencies mock
    // =============================================
    private readonly Mock<IRoleRepository> _roleRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ICacheService> _cache = new();

    private readonly RoleService _sut;

    public RoleServiceTests()
    {
        _sut = new RoleService(
            _roleRepo.Object,
            _userRepo.Object,
            _cache.Object);
    }

    // =============================================
    // Helper
    // =============================================
    private static Role CreateFakeRole(int id = 1, string name = "Admin")
        => new() { Id = id, Name = name };

    private static Role CreateFakeRoleWithPermissions(
        int id,
        string name,
        params string[] permissionNames)
    {
        var role = new Role { Id = id, Name = name };
        foreach (var permName in permissionNames)
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = id,
                Permission = new Permission { Name = permName }
            });
        }

        return role;
    }

    private static User CreateFakeUser(int id = 1, string email = "user@example.com")
        => new()
        {
            Id = id,
            Email = email,
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = "User" } }
            }
        };

    // =============================================
    // CREATEROLE TESTS
    // =============================================

    [Fact]
    public async Task CreateRoleAsync_WhenValidName_AddsRoleAndReturnsId()
    {
        // Arrange
        Role? savedRole = null;

        _roleRepo.Setup(r => r.GetByNameAsync("Manager"))
            .ReturnsAsync((Role?)null);
        _roleRepo.Setup(r => r.AddAsync(It.IsAny<Role>()))
            .Callback<Role>(r => savedRole = r)
            .Returns(Task.CompletedTask);

        // Act
        var id = await _sut.CreateRoleAsync("Manager");

        // Assert
        savedRole.Should().NotBeNull();
        savedRole!.Name.Should().Be("Manager");

        _roleRepo.Verify(r => r.AddAsync(It.IsAny<Role>()), Times.Once);

        // Id = 0 vì chưa qua DB thật
        id.Should().Be(0);
    }

    [Fact]
    public async Task CreateRoleAsync_WhenNameIsEmpty_ThrowsArgumentException()
    {
        var act = () => _sut.CreateRoleAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public async Task CreateRoleAsync_WhenDuplicateName_ThrowsException()
    {
        _roleRepo.Setup(r => r.GetByNameAsync("Admin"))
            .ReturnsAsync(CreateFakeRole(1, "Admin"));

        var act = () => _sut.CreateRoleAsync("Admin");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Role name already exists*");

        _roleRepo.Verify(r => r.AddAsync(It.IsAny<Role>()), Times.Never);
    }

    // =============================================
    // ASSIGNPERMISSIONS TESTS
    // =============================================

    [Fact]
    public async Task AssignPermissionsAsync_WhenValidInput_AssignsAndInvalidatesCache()
    {
        // Arrange
        var roleId = 1;
        var permissionIds = new List<int> { 1, 2, 3 };
        var affectedUserIds = new List<int> { 10, 20 };

        _roleRepo.Setup(r => r.GetByIdAsync(roleId))
            .ReturnsAsync(CreateFakeRole(roleId));
        _roleRepo.Setup(r => r.AssignPermissionsAsync(roleId, permissionIds))
            .Returns(Task.CompletedTask);
        _roleRepo.Setup(r => r.GetUserIdsByRoleIdAsync(roleId))
            .ReturnsAsync(affectedUserIds);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.AssignPermissionsAsync(roleId, permissionIds);

        // Assert — cache phải bị xóa cho từng user bị ảnh hưởng
        _cache.Verify(c => c.RemoveAsync("permissions:10"), Times.Once);
        _cache.Verify(c => c.RemoveAsync("permissions:20"), Times.Once);

        // Tổng số lần xóa cache = số lượng user bị ảnh hưởng
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AssignPermissionsAsync_WhenNoAffectedUsers_DoesNotRemoveCache()
    {
        // Arrange
        _roleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(CreateFakeRole(1));
        _roleRepo.Setup(r => r.AssignPermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        // Không có user nào dùng role này
        _roleRepo.Setup(r => r.GetUserIdsByRoleIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<int>());

        // Act
        await _sut.AssignPermissionsAsync(1, new List<int> { 1 });

        // Assert — không có user → không xóa cache
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AssignPermissionsAsync_WhenEmptyPermissionIds_DoesNotCallRepositoryOrInvalidateCache()
    {
        await _sut.AssignPermissionsAsync(1, Array.Empty<int>());
        await _sut.AssignPermissionsAsync(1, new List<int>());

        _roleRepo.Verify(
            r => r.AssignPermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AssignPermissionsAsync_WhenRoleNotFound_ThrowsException()
    {
        _roleRepo.Setup(r => r.GetByIdAsync(99))
            .ReturnsAsync((Role?)null);

        var act = () => _sut.AssignPermissionsAsync(99, new List<int> { 1 });

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Role not found*");

        _roleRepo.Verify(
            r => r.AssignPermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
    }

    [Fact]
    public async Task AssignPermissionsAsync_WhenPermissionAlreadyAssigned_ThrowsException()
    {
        var roleId = 1;
        var role = new Role { Id = roleId, Name = "Admin" };
        role.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = 2,
            Permission = new Permission { Id = 2, Name = "x" }
        });

        _roleRepo.Setup(r => r.GetByIdAsync(roleId))
            .ReturnsAsync(role);

        var act = () => _sut.AssignPermissionsAsync(roleId, new List<int> { 1, 2, 3 });

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Permission(s) already assigned to role: 2*");

        _roleRepo.Verify(
            r => r.AssignPermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
    }

    // =============================================
    // REMOVEPERMISSIONS TESTS
    // =============================================

    [Fact]
    public async Task RemovePermissionsAsync_WhenValidInput_RemovesAndInvalidatesCache()
    {
        var roleId = 1;
        var permissionIds = new List<int> { 1, 2 };
        var affectedUserIds = new List<int> { 10 };

        var role = new Role { Id = roleId, Name = "Admin" };
        role.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = 1,
            Permission = new Permission { Id = 1, Name = "a" }
        });
        role.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = 2,
            Permission = new Permission { Id = 2, Name = "b" }
        });
        _roleRepo.Setup(r => r.GetByIdAsync(roleId))
            .ReturnsAsync(role);

        _roleRepo.Setup(r => r.RemovePermissionsAsync(
                roleId,
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(permissionIds))))
            .Returns(Task.CompletedTask);
        _roleRepo.Setup(r => r.GetUserIdsByRoleIdAsync(roleId))
            .ReturnsAsync(affectedUserIds);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _sut.RemovePermissionsAsync(roleId, permissionIds);

        _roleRepo.Verify(
            r => r.RemovePermissionsAsync(
                roleId,
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(permissionIds))),
            Times.Once);
        _cache.Verify(c => c.RemoveAsync("permissions:10"), Times.Once);
    }

    [Fact]
    public async Task RemovePermissionsAsync_WhenEmptyPermissionIds_DoesNotCallRepositoryOrInvalidateCache()
    {
        await _sut.RemovePermissionsAsync(1, Array.Empty<int>());

        _roleRepo.Verify(
            r => r.RemovePermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RemovePermissionsAsync_WhenRoleNotFound_ThrowsException()
    {
        _roleRepo.Setup(r => r.GetByIdAsync(99))
            .ReturnsAsync((Role?)null);

        var act = () => _sut.RemovePermissionsAsync(99, new List<int> { 1 });

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Role not found*");

        _roleRepo.Verify(
            r => r.RemovePermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
    }

    [Fact]
    public async Task RemovePermissionsAsync_WhenPermissionNotOnRole_ThrowsException()
    {
        var roleId = 1;
        var role = new Role { Id = roleId, Name = "Admin" };
        role.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = 1,
            Permission = new Permission { Id = 1, Name = "a" }
        });
        _roleRepo.Setup(r => r.GetByIdAsync(roleId))
            .ReturnsAsync(role);

        var act = () => _sut.RemovePermissionsAsync(roleId, new List<int> { 1, 99 });

        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*Permission(s) not assigned to role: 99*");

        _roleRepo.Verify(
            r => r.RemovePermissionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
    }

    // =============================================
    // ASSIGNROLETOUSERASYNC TESTS
    // =============================================

    [Fact]
    public async Task AssignRoleToUserAsync_WhenValidInput_AssignsAndInvalidatesCache()
    {
        // Arrange
        _userRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(CreateFakeUser(1));
        _roleRepo.Setup(r => r.GetByIdAsync(2))
            .ReturnsAsync(CreateFakeRole(2, "Manager"));
        _roleRepo.Setup(r => r.AssignRoleToUserAsync(1, 2))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.AssignRoleToUserAsync(userId: 1, roleId: 2);

        // Assert
        _roleRepo.Verify(r => r.AssignRoleToUserAsync(1, 2), Times.Once);

        // Cache của đúng userId phải bị xóa
        _cache.Verify(c => c.RemoveAsync("permissions:1"), Times.Once);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_CacheKeyContainsCorrectUserId()
    {
        // Arrange
        var userId = 42;

        _userRepo.Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(CreateFakeUser(userId));
        _roleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(CreateFakeRole(1));
        _roleRepo.Setup(r => r.AssignRoleToUserAsync(userId, It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.AssignRoleToUserAsync(userId, roleId: 1);

        // Assert — key phải chứa đúng userId
        _cache.Verify(c => c.RemoveAsync($"permissions:{userId}"), Times.Once);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_WhenUserNotFound_ThrowsException()
    {
        _userRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((User?)null);

        var act = () => _sut.AssignRoleToUserAsync(1, 2);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*User not found*");

        _roleRepo.Verify(r => r.AssignRoleToUserAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_WhenRoleNotFound_ThrowsException()
    {
        _userRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(CreateFakeUser(1));
        _roleRepo.Setup(r => r.GetByIdAsync(2))
            .ReturnsAsync((Role?)null);

        var act = () => _sut.AssignRoleToUserAsync(1, 2);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Role not found*");

        _roleRepo.Verify(r => r.AssignRoleToUserAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // =============================================
    // GETROLES TESTS
    // =============================================

    [Fact]
    public async Task GetRolesAsync_WhenRolesExist_ReturnsMappedTuplesWithPermissions()
    {
        // Arrange
        var roles = new List<Role>
        {
            CreateFakeRoleWithPermissions(1, "Admin", "order.read", "product.delete"),
            CreateFakeRoleWithPermissions(2, "User", "order.create")
        };

        _roleRepo.Setup(r => r.GetAllWithPermissionsAsync())
            .ReturnsAsync(roles);

        // Act
        var result = await _sut.GetRolesAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Admin");
        result[0].Permissions.Should().Equal("order.read", "product.delete");
        result[1].Id.Should().Be(2);
        result[1].Name.Should().Be("User");
        result[1].Permissions.Should().Equal("order.create");
    }

    [Fact]
    public async Task GetRolesAsync_WhenNoRoles_ReturnsEmptyList()
    {
        // Arrange
        _roleRepo.Setup(r => r.GetAllWithPermissionsAsync())
            .ReturnsAsync(new List<Role>());

        // Act
        var result = await _sut.GetRolesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // =============================================
    // GETALLUSERSWITHROLESASYNC TESTS
    // =============================================

    [Fact]
    public async Task GetAllUsersWithRolesAsync_WhenUsersExist_ReturnsMappedDtos()
    {
        // Arrange
        var users = new List<User>
        {
            CreateFakeUser(1, "admin@example.com"),
            CreateFakeUser(2, "user@example.com")
        };

        _userRepo.Setup(r => r.GetAllWithRolesAsync())
            .ReturnsAsync(users);

        // Act
        var result = await _sut.GetAllUsersWithRolesAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Email.Should().Be("admin@example.com");
        result[0].Role.Should().Be("User"); // ← từ CreateFakeUser
        result[1].Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task GetAllUsersWithRolesAsync_WhenUserHasNoRole_ReturnsNone()
    {
        // Arrange — user không có role nào
        var users = new List<User>
        {
            new()
            {
                Id = 1,
                Email = "norole@example.com",
                UserRoles = new List<UserRole>() // ← rỗng
            }
        };

        _userRepo.Setup(r => r.GetAllWithRolesAsync())
            .ReturnsAsync(users);

        // Act
        var result = await _sut.GetAllUsersWithRolesAsync();

        // Assert — user không có role → hiển thị "None"
        result[0].Role.Should().Be("None");
    }

    [Fact]
    public async Task GetAllUsersWithRolesAsync_WhenNoUsers_ReturnsEmptyList()
    {
        // Arrange
        _userRepo.Setup(r => r.GetAllWithRolesAsync())
            .ReturnsAsync(new List<User>());

        // Act
        var result = await _sut.GetAllUsersWithRolesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // =============================================
    // UPDATEROLE TESTS
    // =============================================

    [Fact]
    public async Task UpdateRoleAsync_WhenRoleNotFound_ThrowsException()
    {
        // Arrange
        _roleRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Role?)null);

        // Act
        var act = () => _sut.UpdateRoleAsync(99, "NewName");

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Role not found*");
    }

    [Fact]
    public async Task UpdateRoleAsync_WhenValidInput_UpdatesNameCorrectly()
    {
        // Arrange
        var role = CreateFakeRole(1, "OldName");

        _roleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(role);
        _roleRepo.Setup(r => r.UpdateAsync(It.IsAny<Role>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.UpdateRoleAsync(1, "NewName");

        // Assert
        role.Name.Should().Be("NewName");
        _roleRepo.Verify(r => r.UpdateAsync(role), Times.Once);
    }

    // =============================================
    // DELETEROLE TESTS
    // =============================================

    [Fact]
    public async Task DeleteRoleAsync_WhenRoleNotFound_ThrowsException()
    {
        // Arrange
        _roleRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Role?)null);

        // Act
        var act = () => _sut.DeleteRoleAsync(99);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Role not found*");
    }

    [Fact]
    public async Task DeleteRoleAsync_WhenRoleExists_DeletesSuccessfully()
    {
        // Arrange
        var role = CreateFakeRole();

        _roleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(role);
        _roleRepo.Setup(r => r.DeleteAsync(It.IsAny<Role>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteRoleAsync(1);

        // Assert
        _roleRepo.Verify(r => r.DeleteAsync(role), Times.Once);
    }
}