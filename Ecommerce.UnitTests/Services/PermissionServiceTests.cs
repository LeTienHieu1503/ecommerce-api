using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

public class PermissionServiceTests
{
    // =============================================
    // Dependencies mock
    // =============================================
    private readonly Mock<IPermissionRepository> _permissionRepo = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<PermissionService>> _logger = new();
    private readonly Mock<IRequestDeviceContext> _requestDeviceContext = new();

    private readonly PermissionService _sut;

    public PermissionServiceTests()
    {
        _sut = new PermissionService(
            _permissionRepo.Object,
            _cache.Object,
            _logger.Object,
            _requestDeviceContext.Object);
    }

    // =============================================
    // Helper
    // =============================================
    private static Permission CreateFakePermission(
        int id = 1,
        string entity = "product",
        string action = "read")
        => new()
        {
            Id = id,
            Entity = entity,
            Action = action,
            Name = $"{entity}.{action}"
        };

    // =============================================
    // GETUSERPERMISSIONS TESTS
    // =============================================

    [Fact]
    public async Task GetUserPermissionsAsync_WhenCacheHit_ReturnsCachedPermissions()
    {
        // Arrange
        var cached = new List<string> { "product.read", "product.create" };

        _cache.Setup(c => c.GetAsync<List<string>>(It.IsAny<string>()))
            .ReturnsAsync(cached);

        // Act
        var result = await _sut.GetUserPermissionsAsync(1);

        // Assert
        result.Should().BeEquivalentTo(cached);

        // Repository KHÔNG được gọi vì có cache
        _permissionRepo.Verify(
            r => r.GetPermissionsByUserIdAsync(It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WhenCacheMiss_QueriesRepositoryAndSetsCache()
    {
        // Arrange
        var permissions = new List<string> { "product.read", "order.create" };

        // Lần 1 và 2 đều miss cache
        _cache.Setup(c => c.GetAsync<List<string>>(It.IsAny<string>()))
            .ReturnsAsync((List<string>?)null);

        _permissionRepo.Setup(r => r.GetPermissionsByUserIdAsync(1))
            .ReturnsAsync(permissions);

        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetUserPermissionsAsync(1);

        // Assert
        result.Should().BeEquivalentTo(permissions);

        // Cache phải được set
        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WhenUserHasNoPermissions_DoesNotSetCache()
    {
        // Arrange — user không có permission nào
        _cache.Setup(c => c.GetAsync<List<string>>(It.IsAny<string>()))
            .ReturnsAsync((List<string>?)null);

        _permissionRepo.Setup(r => r.GetPermissionsByUserIdAsync(1))
            .ReturnsAsync(new List<string>()); // ← rỗng

        // Act
        var result = await _sut.GetUserPermissionsAsync(1);

        // Assert
        result.Should().BeEmpty();

        // Cache KHÔNG được set khi không có permission
        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WhenCacheReturnEmptyList_QueriesRepository()
    {
        // Arrange — cache trả về list rỗng → vẫn phải query DB
        _cache.Setup(c => c.GetAsync<List<string>>(It.IsAny<string>()))
            .ReturnsAsync(new List<string>()); // ← rỗng, không phải null

        var permissions = new List<string> { "product.read" };
        _permissionRepo.Setup(r => r.GetPermissionsByUserIdAsync(1))
            .ReturnsAsync(permissions);

        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetUserPermissionsAsync(1);

        // Assert — cache rỗng → query DB
        _permissionRepo.Verify(
            r => r.GetPermissionsByUserIdAsync(1),
            Times.Once);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_CacheKeyContainsUserId()
    {
        // Arrange
        var userId = 42;
        var expectedCacheKey = $"permissions:{userId}";

        _cache.Setup(c => c.GetAsync<List<string>>(expectedCacheKey))
            .ReturnsAsync(new List<string> { "product.read" });

        // Act
        await _sut.GetUserPermissionsAsync(userId);

        // Assert — cache phải dùng đúng key chứa userId
        _cache.Verify(
            c => c.GetAsync<List<string>>(expectedCacheKey),
            Times.Once);
    }

    // =============================================
    // GETALLPERMISSIONS TESTS
    // =============================================

    [Fact]
    public async Task GetAllPermissionsAsync_WhenPermissionsExist_ReturnsMappedDtos()
    {
        // Arrange
        var permissions = new List<Permission>
        {
            CreateFakePermission(1, "product", "read"),
            CreateFakePermission(2, "order", "create")
        };

        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(permissions);

        // Act
        var result = await _sut.GetAllPermissionsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("product.read");
        result[1].Name.Should().Be("order.create");
    }

    [Fact]
    public async Task GetAllPermissionsAsync_WhenNoPermissions_ReturnsEmptyList()
    {
        // Arrange
        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Permission>());

        // Act
        var result = await _sut.GetAllPermissionsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllPermissionsAsync_MapsFieldsCorrectly()
    {
        // Arrange
        var permissions = new List<Permission>
        {
            CreateFakePermission(id: 5, entity: "category", action: "delete")
        };

        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(permissions);

        // Act
        var result = await _sut.GetAllPermissionsAsync();

        // Assert — kiểm tra mapping từng field
        var dto = result[0];
        dto.Id.Should().Be(5);
        dto.Entity.Should().Be("category");
        dto.Action.Should().Be("delete");
        dto.Name.Should().Be("category.delete");
    }

    // =============================================
    // CREATEPERMISSION TESTS
    // =============================================

    [Fact]
    public async Task CreatePermissionAsync_WhenValidInput_AddsPermissionWithCorrectName()
    {
        // Arrange
        Permission? savedPermission = null;

        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Permission>());
        _permissionRepo.Setup(r => r.AddAsync(It.IsAny<Permission>()))
            .Callback<Permission>(p => savedPermission = p) // ← capture object được save
            .Returns(Task.CompletedTask);

        // Act
        await _sut.CreatePermissionAsync("product", "create");

        // Assert — Name phải được ghép từ entity.action
        savedPermission.Should().NotBeNull();
        savedPermission!.Entity.Should().Be("product");
        savedPermission.Action.Should().Be("create");
        savedPermission.Name.Should().Be("product.create");

        _permissionRepo.Verify(r => r.AddAsync(It.IsAny<Permission>()), Times.Once);
    }

    [Fact]
    public async Task CreatePermissionAsync_WhenValidInput_ReturnsPermissionId()
    {
        // Arrange
        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Permission>());
        _permissionRepo.Setup(r => r.AddAsync(It.IsAny<Permission>()))
            .Returns(Task.CompletedTask);

        // Act
        var id = await _sut.CreatePermissionAsync("order", "read");

        // Assert — Id mặc định là 0 khi chưa qua DB thật
        // Trong Unit Test không có DB nên Id = 0 là đúng
        id.Should().Be(0);
    }

    [Fact]
    public async Task CreatePermissionAsync_WhenDuplicateEntityAndAction_ThrowsException()
    {
        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Permission>
            {
                CreateFakePermission(1, "product", "read")
            });

        var act = () => _sut.CreatePermissionAsync("product", "read");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Permission already exists*");

        _permissionRepo.Verify(r => r.AddAsync(It.IsAny<Permission>()), Times.Never);
    }

    // =============================================
    // UPDATEPERMISSION TESTS
    // =============================================

    [Fact]
    public async Task UpdatePermissionAsync_WhenPermissionNotFound_ThrowsException()
    {
        // Arrange
        _permissionRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var act = () => _sut.UpdatePermissionAsync(99, "product", "update");

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Permission not found*");
    }

    [Fact]
    public async Task UpdatePermissionAsync_WhenValidInput_UpdatesFieldsCorrectly()
    {
        // Arrange
        var permission = CreateFakePermission(1, "product", "read");

        _permissionRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(permission);
        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Permission> { permission });
        _permissionRepo.Setup(r => r.UpdateAsync(It.IsAny<Permission>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.UpdatePermissionAsync(1, "order", "delete");

        // Assert — fields phải được update đúng
        permission.Entity.Should().Be("order");
        permission.Action.Should().Be("delete");
        permission.Name.Should().Be("order.delete");

        _permissionRepo.Verify(r => r.UpdateAsync(permission), Times.Once);
    }

    [Fact]
    public async Task UpdatePermissionAsync_WhenEntityActionConflictsWithAnotherPermission_ThrowsException()
    {
        var permission = CreateFakePermission(1, "product", "read");
        var other = CreateFakePermission(2, "order", "create");

        _permissionRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(permission);
        _permissionRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Permission> { permission, other });

        var act = () => _sut.UpdatePermissionAsync(1, "order", "create");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Permission already exists*");

        _permissionRepo.Verify(r => r.UpdateAsync(It.IsAny<Permission>()), Times.Never);
    }

    // =============================================
    // DELETEPERMISSION TESTS
    // =============================================

    [Fact]
    public async Task DeletePermissionAsync_WhenPermissionNotFound_ThrowsException()
    {
        // Arrange
        _permissionRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var act = () => _sut.DeletePermissionAsync(99);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Permission not found*");
    }

    [Fact]
    public async Task DeletePermissionAsync_WhenPermissionExists_DeletesSuccessfully()
    {
        // Arrange
        var permission = CreateFakePermission();

        _permissionRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(permission);
        _permissionRepo.Setup(r => r.DeleteAsync(It.IsAny<Permission>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.DeletePermissionAsync(1);

        // Assert
        _permissionRepo.Verify(r => r.DeleteAsync(permission), Times.Once);
    }
}