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

        _cacheMock.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync((long?)0);

        _cacheMock.Setup(c => c.GetAsync<PagedResult<CategoryResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<CategoryResponseDto>?)null);

        _repositoryMock.Setup(r => r.GetQueryable())
            .Returns(new List<Category>
            {
                new Category{ Id=1, Name="Phone"}
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
        var dto = new CategoryResponseDto { Id = 1, Name = "Phone" };

        _cacheMock.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync(dto);

        var result = await _service.GetByIdAsync(1);

        result.Should().BeEquivalentTo(dto);

        _repositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldQueryDatabase_WhenCacheMiss()
    {
        _cacheMock.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((CategoryResponseDto?)null);

        _repositoryMock.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new Category { Id = 1, Name = "Phone" });

        var result = await _service.GetByIdAsync(1);

        result.Name.Should().Be("Phone");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenCategoryNotExist()
    {
        _cacheMock.Setup(c => c.GetAsync<CategoryResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((CategoryResponseDto?)null);

        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetByIdAsync(1));
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
        var category = new Category { Id = 1, Name = "Old" };

        _repositoryMock.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        var result = await _service.UpdateAsync(1,
            new UpdateCategoryDto { Name = "New" });

        result.Name.Should().Be("New");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFound_WhenCategoryNotExist()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateAsync(1, new UpdateCategoryDto { Name = "Test" }));
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDeleteCategory()
    {
        var category = new Category { Id = 1 };

        _repositoryMock.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        _repositoryMock.Setup(r => r.HasProductsAsync(1))
            .ReturnsAsync(false);

        await _service.DeleteAsync(1);

        category.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowBusinessException_WhenHasProducts()
    {
        var category = new Category { Id = 1 };

        _repositoryMock.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        _repositoryMock.Setup(r => r.HasProductsAsync(1))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.DeleteAsync(1));
    }
}