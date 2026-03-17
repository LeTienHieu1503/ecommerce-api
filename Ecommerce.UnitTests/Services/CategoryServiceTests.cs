using Ecommerce.Application.DTOs.Category;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Common.Caching;
using Ecommerce.Domain.Common.Pagination;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class CategoryServiceTests
{
    private readonly Mock<ICategoryRepository> _repositoryMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<ILogger<CategoryService>> _loggerMock = new();

    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _service = new CategoryService(
            _repositoryMock.Object,
            _loggerMock.Object,
            _cacheMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_ShouldQueryDatabase_WhenCacheMiss()
    {
        var query = new CategoryQuery { Page = 1, PageSize = 10 };
        var categoryId = new System.Random().Next(1, 10000);

        _cacheMock.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync((long?)0);

        _cacheMock.Setup(c => c.GetAsync<PagedResult<CategoryResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<CategoryResponseDto>?)null);

        _repositoryMock.Setup(r => r.GetQueryable())
            .Returns(new List<Category>
            {
                new Category{ Id = categoryId, Name = "Phone" }
            }.AsQueryable());

        var result = await _service.GetAllAsync(query);

        result.Should().NotBeNull();

        _repositoryMock.Verify(r => r.GetQueryable(), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnCache_WhenCacheHit()
    {
        var query = new CategoryQuery();

        var cached = new PagedResult<CategoryResponseDto>(
            new List<CategoryResponseDto>(),
            1,
            10,
            0
        );

        _cacheMock.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(0);

        _cacheMock.Setup(c => c.GetAsync<PagedResult<CategoryResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync(cached);

        var result = await _service.GetAllAsync(query);

        result.Should().BeEquivalentTo(cached);

        _repositoryMock.Verify(r => r.GetQueryable(), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnCache_WhenCacheExists()
    {
        var categoryId = new System.Random().Next(1, 10000);
        var dto = new CategoryResponseDto { Id = categoryId, Name = "Phone" };

        _cacheMock.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync(dto);

        var result = await _service.GetByIdAsync(categoryId);

        result.Should().BeEquivalentTo(dto);

        _repositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldQueryDatabase_WhenCacheMiss()
    {
        var categoryId = new System.Random().Next(1, 10000);

        _cacheMock.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((CategoryResponseDto?)null);

        _repositoryMock.Setup(r => r.GetByIdAsync(categoryId))
            .ReturnsAsync(new Category { Id = categoryId, Name = "Phone" });

        var result = await _service.GetByIdAsync(categoryId);

        result.Name.Should().Be("Phone");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenCategoryNotExist()
    {
        var categoryId = new System.Random().Next(1, 10000);

        _cacheMock.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((CategoryResponseDto?)null);

        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetByIdAsync(categoryId));
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateCategory()
    {
        var dto = new CreateCategoryDto { Name = "Laptop" };

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<Category>()))
            .Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(dto);

        result.Name.Should().Be("Laptop");

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Category>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateCategory()
    {
        var categoryId = new System.Random().Next(1, 10000);
        var category = new Category { Id = categoryId, Name = "Old" };

        _repositoryMock.Setup(r => r.GetByIdAsync(categoryId))
            .ReturnsAsync(category);

        var result = await _service.UpdateAsync(categoryId,
            new UpdateCategoryDto { Name = "New" });

        result.Name.Should().Be("New");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFound_WhenCategoryNotExist()
    {
        var categoryId = new System.Random().Next(1, 10000);

        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateAsync(categoryId, new UpdateCategoryDto { Name = "Test" }));
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDeleteCategory()
    {
        var categoryId = new System.Random().Next(1, 10000);
        var category = new Category { Id = categoryId };

        _repositoryMock.Setup(r => r.GetByIdAsync(categoryId))
            .ReturnsAsync(category);

        _repositoryMock.Setup(r => r.HasProductsAsync(categoryId))
            .ReturnsAsync(false);

        await _service.DeleteAsync(categoryId);

        category.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowBusinessException_WhenHasProducts()
    {
        var categoryId = new System.Random().Next(1, 10000);
        var category = new Category { Id = categoryId };

        _repositoryMock.Setup(r => r.GetByIdAsync(categoryId))
            .ReturnsAsync(category);

        _repositoryMock.Setup(r => r.HasProductsAsync(categoryId))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.DeleteAsync(categoryId));
    }
}
