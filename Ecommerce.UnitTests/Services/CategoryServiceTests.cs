using System.Linq.Expressions;
using Ecommerce.Application.Common.Caching;
using Ecommerce.Application.DTOs.Category;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

public class CategoryServiceTests
{
    // =============================================
    // Dependencies mock
    // =============================================
    private readonly Mock<ICategoryRepository> _categoryRepo = new();
    private readonly Mock<ILogger<CategoryService>> _logger = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IRequestDeviceContext> _requestDeviceContext = new();

    // Service thật — inject mock vào
    private readonly CategoryService _sut;

    public CategoryServiceTests()
    {
        _sut = new CategoryService(
            _categoryRepo.Object,
            _logger.Object,
            _cache.Object,
            _requestDeviceContext.Object);
    }

    // =============================================
    // Helper — tạo Category giả
    // =============================================
    private static Category CreateFakeCategory(int id = 1, string name = "Electronics")
        => new()
        {
            Id = id,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    // Lỗi xảy ra ở đây là vì constructor của PagedResult<CategoryResponseDto> yêu cầu truyền các tham số bắt buộc
    // (IEnumerable<CategoryResponseDto> items, int totalCount, int page, int pageSize), 
    // nhưng code hiện tại dùng object initializer mà không có constructor mặc định phù hợp.
    // Để sửa, cần truyền đủ các tham số cho constructor như sau:

    private static PagedResult<CategoryResponseDto> CreateFakePagedResult()
        => new(
            new List<CategoryResponseDto>
            {
                new() { Id = 1, Name = "Electronics" }
            },
            1, // TotalCount
            1, // Page
            10 // PageSize
        );
    // =============================================
    // GETALL TESTS
    // =============================================

    [Fact]
    public async Task GetAllAsync_WhenCacheHit_ReturnsCachedResult()
    {
        // Arrange
        var fakeResult = CreateFakePagedResult();
        var query = new CategoryQuery { Page = 1, PageSize = 10 };

        // Cache version trả về 1
        _cache.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(1L);

        // Cache có data → trả về luôn
        _cache.Setup(c => c.GetAsync<PagedResult<CategoryResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync(fakeResult);

        // Act
        var result = await _sut.GetAllAsync(query);

        // Assert
        result.Should().BeEquivalentTo(fakeResult);

        // Repository KHÔNG được gọi vì có cache
        _categoryRepo.Verify(r => r.GetQueryable(), Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_WhenCacheMiss_QueriesRepositoryAndSetsCache()
    {
        // Arrange
        var query = new CategoryQuery { Page = 1, PageSize = 10 };

        // Cache version trả về 0
        _cache.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync((long?)null);

        // Cache miss — không có data
        _cache.Setup(c => c.GetAsync<PagedResult<CategoryResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<CategoryResponseDto>?)null);

        // Repository trả về danh sách category
        var categories = new List<Category> { CreateFakeCategory() }.AsQueryable();
        _categoryRepo.Setup(r => r.GetQueryable())
            .Returns(categories);

        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<PagedResult<CategoryResponseDto>>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetAllAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);

        // Cache phải được set sau khi query DB
        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<PagedResult<CategoryResponseDto>>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    // =============================================
    // GETBYID TESTS
    // =============================================

    [Fact]
    public async Task GetByIdAsync_WhenCacheHit_ReturnsCachedCategory()
    {
        // Arrange
        var fakeDto = new CategoryResponseDto { Id = 1, Name = "Electronics" };

        _cache.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync(fakeDto);

        // Act
        var result = await _sut.GetByIdAsync(1);

        // Assert
        result.Should().BeEquivalentTo(fakeDto);

        // Repository KHÔNG được gọi
        _categoryRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCacheMiss_QueriesRepositoryAndSetsCache()
    {
        // Arrange
        var category = CreateFakeCategory();

        _cache.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((CategoryResponseDto?)null);

        _categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<CategoryResponseDto>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Electronics");

        // Cache phải được set
        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<CategoryResponseDto>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCategoryNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _cache.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((CategoryResponseDto?)null);

        _categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        // Act
        var act = () => _sut.GetByIdAsync(99);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Category not found*");
    }

    // =============================================
    // CREATE TESTS
    // =============================================

    [Fact]
    public async Task CreateAsync_WhenValidRequest_ReturnsCategoryDto()
    {
        // Arrange
        var dto = new CreateCategoryDto { Name = "New Category" };

        _categoryRepo.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<Category, bool>>>()))
            .ReturnsAsync(false);
        _categoryRepo.Setup(r => r.AddAsync(It.IsAny<Category>()))
            .Returns(Task.CompletedTask);
        _categoryRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // BumpVersion không throw
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("New Category");

        _categoryRepo.Verify(r => r.AddAsync(It.IsAny<Category>()), Times.Once);
        _categoryRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenCacheIncrementFails_StillReturnsResult()
    {
        // Arrange
        var dto = new CreateCategoryDto { Name = "New Category" };

        _categoryRepo.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<Category, bool>>>()))
            .ReturnsAsync(false);
        _categoryRepo.Setup(r => r.AddAsync(It.IsAny<Category>()))
            .Returns(Task.CompletedTask);
        _categoryRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Cache lỗi — nhưng không được ảnh hưởng kết quả
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new Exception("Redis down"));

        // Act
        var act = () => _sut.CreateAsync(dto);

        // Assert — Cache lỗi nhưng Create KHÔNG throw
        await act.Should().NotThrowAsync();
    }

    // =============================================
    // UPDATE TESTS
    // =============================================

    [Fact]
    public async Task UpdateAsync_WhenCategoryNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        var dto = new UpdateCategoryDto { Name = "Updated" };

        // Act
        var act = () => _sut.UpdateAsync(99, dto);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Category not found*");
    }

    [Fact]
    public async Task UpdateAsync_WhenValidRequest_UpdatesAndInvalidatesCache()
    {
        // Arrange
        var category = CreateFakeCategory();
        var dto = new UpdateCategoryDto { Name = "Updated Name" };

        _categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);
        _categoryRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _sut.UpdateAsync(1, dto);

        // Assert
        result.Name.Should().Be("Updated Name");

        // Cache của category đó phải bị xóa
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Once);
    }

    // =============================================
    // DELETE TESTS
    // =============================================

    [Fact]
    public async Task DeleteAsync_WhenCategoryNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        // Act
        var act = () => _sut.DeleteAsync(99);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Category not found*");
    }

    [Fact]
    public async Task DeleteAsync_WhenCategoryHasProducts_ThrowsBusinessException()
    {
        // Arrange — Fail-Fast: category có products → không cho xóa
        var category = CreateFakeCategory();

        _categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);
        _categoryRepo.Setup(r => r.HasProductsAsync(1))
            .ReturnsAsync(true); // ← có products

        // Act
        var act = () => _sut.DeleteAsync(1);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*Cannot delete*products*");
    }

    [Fact]
    public async Task DeleteAsync_WhenValidRequest_SoftDeletesAndInvalidatesCache()
    {
        // Arrange
        var category = CreateFakeCategory();

        _categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);
        _categoryRepo.Setup(r => r.HasProductsAsync(1))
            .ReturnsAsync(false); // ← không có products
        _categoryRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        await _sut.DeleteAsync(1);

        // Assert — Soft delete: IsDeleted = true, không xóa khỏi DB
        category.IsDeleted.Should().BeTrue();
        _categoryRepo.Verify(r => r.SaveChangesAsync(), Times.Once);

        // Cache phải bị xóa
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenCacheFails_StillDeletesCategory()
    {
        // Arrange
        var category = CreateFakeCategory();

        _categoryRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(category);
        _categoryRepo.Setup(r => r.HasProductsAsync(1)).ReturnsAsync(false);
        _categoryRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        // Cache lỗi
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Redis down"));
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new Exception("Redis down"));

        // Act
        var act = () => _sut.DeleteAsync(1);

        // Assert
        await act.Should().NotThrowAsync();
        category.IsDeleted.Should().BeTrue();
    }
}