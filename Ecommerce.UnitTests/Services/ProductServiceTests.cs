using Ecommerce.Application.DTOs.Product;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;

public class ProductServiceTests
{
    // =============================================
    // Dependencies mock
    // =============================================
    private readonly Mock<IProductRepository> _productRepo = new();
    private readonly Mock<ILogger<ProductService>> _logger = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IRequestDeviceContext> _requestDeviceContext = new();

    private readonly ProductService _sut;

    public ProductServiceTests()
    {
        _sut = new ProductService(
            _productRepo.Object,
            _logger.Object,
            _cache.Object,
            _requestDeviceContext.Object);
    }

    // =============================================
    // Helper
    // =============================================
    private static Product CreateFakeProduct(
        int id = 1,
        string name = "Product A",
        decimal price = 100m,
        int stock = 10,
        int categoryId = 1)
        => new()
        {
            Id = id,
            Name = name,
            Price = price,
            Stock = stock,
            CategoryId = categoryId,
            Category = new Category { Id = categoryId, Name = "Electronics" },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static ProductResponseDto CreateFakeProductDto(int id = 1)
        => new()
        {
            Id = id,
            Name = "Product A",
            Price = 100m,
            Stock = 10,
            CategoryId = 1,
            CategoryName = "Electronics"
        };

    private static PagedResult<ProductResponseDto> CreateFakePagedResult()
        => new(
            new List<ProductResponseDto> { CreateFakeProductDto() },
            totalCount: 1,
            page: 1,
            pageSize: 10
        );
    // =============================================
    // CREATE TESTS
    // =============================================

    [Fact]
    public async Task CreateAsync_WhenCategoryNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _productRepo.Setup(r => r.CategoryExistsAsync(It.IsAny<int>()))
            .ReturnsAsync(false); // ← category không tồn tại

        var dto = new CreateProductDto
        {
            Name = "New Product",
            Price = 100m,
            CategoryId = 99,
            Stock = 5
        };

        // Act
        var act = () => _sut.CreateAsync(dto);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Category not found*");
    }

    [Fact]
    public async Task CreateAsync_WhenValidRequest_SavesProductAndReturnsDto()
    {
        // Arrange
        var dto = new CreateProductDto
        {
            Name = "New Product",
            Price = 100m,
            CategoryId = 1,
            Stock = 5
        };

        var savedProduct = CreateFakeProduct();

        _productRepo.Setup(r => r.CategoryExistsAsync(1))
            .ReturnsAsync(true);
        _productRepo.Setup(r => r.AddAsync(It.IsAny<Product>()))
            .Callback<Product>(p => p.Id = savedProduct.Id)
            .Returns(Task.CompletedTask);
        _productRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(savedProduct);

        // Cache miss → query DB
        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto?)null);
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<ProductResponseDto>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("New Product");

        _productRepo.Verify(r => r.AddAsync(It.IsAny<Product>()), Times.Once);
        _productRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenCacheIncrementFails_StillReturnsResult()
    {
        // Arrange
        var dto = new CreateProductDto
        {
            Name = "New Product",
            Price = 100m,
            CategoryId = 1,
            Stock = 5
        };

        _productRepo.Setup(r => r.CategoryExistsAsync(1))
            .ReturnsAsync(true);
        _productRepo.Setup(r => r.AddAsync(It.IsAny<Product>()))
            .Callback<Product>(p => p.Id = 1)
            .Returns(Task.CompletedTask);
        _productRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(CreateFakeProduct());

        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto?)null);
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<ProductResponseDto>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Cache lỗi
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new Exception("Redis down"));

        // Act
        var act = () => _sut.CreateAsync(dto);

        // Assert — cache lỗi nhưng Create KHÔNG throw
        await act.Should().NotThrowAsync();
    }

    // =============================================
    // GETBYID TESTS
    // =============================================

    [Fact]
    public async Task GetByIdAsync_WhenCacheHit_ReturnsCachedProduct()
    {
        // Arrange
        var fakeDto = CreateFakeProductDto();

        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync(fakeDto);

        // Act
        var result = await _sut.GetByIdAsync(1);

        // Assert
        result.Should().BeEquivalentTo(fakeDto);

        // Repository KHÔNG được gọi
        _productRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCacheMiss_QueriesRepositoryAndSetsCache()
    {
        // Arrange
        var product = CreateFakeProduct();

        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto?)null);

        _productRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(product);

        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<ProductResponseDto>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Product A");

        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<ProductResponseDto>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenProductNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto?)null);

        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product?)null);

        // Act
        var act = () => _sut.GetByIdAsync(99);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Product not found*");
    }

    // =============================================
    // GETALL TESTS
    // =============================================

    [Fact]
    public async Task GetAllAsync_WhenCacheHit_ReturnsCachedResult()
    {
        // Arrange
        var fakeResult = CreateFakePagedResult();
        var query = new ProductQuery { Page = 1, PageSize = 10 };

        // Cache version
        _cache.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(1L);

        // Cache có data
        _cache.Setup(c => c.GetAsync<PagedResult<ProductResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync(fakeResult);

        // Act
        var result = await _sut.GetAllAsync(query);

        // Assert
        result.Should().BeEquivalentTo(fakeResult);

        // Repository KHÔNG được gọi
        _productRepo.Verify(r => r.GetQueryable(), Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_WhenCacheMiss_QueriesRepositoryAndSetsCache()
    {
        // Arrange
        var query = new ProductQuery { Page = 1, PageSize = 10 };

        _cache.Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync((long?)null);

        _cache.Setup(c => c.GetAsync<PagedResult<ProductResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<ProductResponseDto>?)null);

        var products = new List<Product> { CreateFakeProduct() }.AsQueryable();
        _productRepo.Setup(r => r.GetQueryable())
            .Returns(products);

        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<PagedResult<ProductResponseDto>>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetAllAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);

        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<PagedResult<ProductResponseDto>>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    // =============================================
    // UPDATE TESTS
    // =============================================

    [Fact]
    public async Task UpdateAsync_WhenProductNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product?)null);

        var dto = new UpdateProductDto
        {
            Name = "Updated",
            Price = 200m,
            CategoryId = 1,
            Stock = 5
        };

        // Act
        var act = () => _sut.UpdateAsync(99, dto);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Product not found*");
    }

    [Fact]
    public async Task UpdateAsync_WhenCategoryNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var product = CreateFakeProduct();

        _productRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(product);
        _productRepo.Setup(r => r.CategoryExistsAsync(It.IsAny<int>()))
            .ReturnsAsync(false); // ← category không tồn tại

        var dto = new UpdateProductDto
        {
            Name = "Updated",
            Price = 200m,
            CategoryId = 99,
            Stock = 5
        };

        // Act
        var act = () => _sut.UpdateAsync(1, dto);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Category not found*");
    }

    [Fact]
    public async Task UpdateAsync_WhenValidRequest_UpdatesFieldsAndInvalidatesCache()
    {
        // Arrange
        var product = CreateFakeProduct();
        var updatedProduct = CreateFakeProduct(name: "Updated Product", price: 200m);

        var dto = new UpdateProductDto
        {
            Name = "Updated Product",
            Price = 200m,
            CategoryId = 1,
            Stock = 8
        };

        _productRepo.SetupSequence(r => r.GetByIdAsync(1))
            .ReturnsAsync(product)        // ← lần gọi 1: lấy product để update
            .ReturnsAsync(updatedProduct); // ← lần gọi 2: lấy product đã update
        _productRepo.Setup(r => r.CategoryExistsAsync(1))
            .ReturnsAsync(true);
        _productRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // GetByIdAsync sau update
        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto?)null);
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<ProductResponseDto>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _sut.UpdateAsync(1, dto);

        // Assert
        result.Should().NotBeNull();

        // Fields phải được update
        product.Name.Should().Be("Updated Product");
        product.Price.Should().Be(200m);
        product.Stock.Should().Be(8);

        // Cache phải bị xóa
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyConflict_ThrowsBusinessException()
    {
        // Arrange
        var product = CreateFakeProduct();

        _productRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(product);
        _productRepo.Setup(r => r.CategoryExistsAsync(1))
            .ReturnsAsync(true);

        // SaveChanges throw concurrency
        _productRepo.Setup(r => r.SaveChangesAsync())
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var dto = new UpdateProductDto
        {
            Name = "Updated",
            Price = 200m,
            CategoryId = 1,
            Stock = 5
        };

        // Act
        var act = () => _sut.UpdateAsync(1, dto);

        // Assert
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*updated by another user*");
    }

    // =============================================
    // DELETE TESTS
    // =============================================

    [Fact]
    public async Task DeleteAsync_WhenProductNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product?)null);

        // Act
        var act = () => _sut.DeleteAsync(99);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Product not found*");
    }

    [Fact]
    public async Task DeleteAsync_WhenValidRequest_SoftDeletesAndInvalidatesCache()
    {
        // Arrange
        var product = CreateFakeProduct();

        _productRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(product);
        _productRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1L);

        // Act
        await _sut.DeleteAsync(1);

        // Assert — Soft delete: IsDeleted = true
        product.IsDeleted.Should().BeTrue();
        _productRepo.Verify(r => r.SaveChangesAsync(), Times.Once);

        // Cache phải bị xóa
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenCacheFails_StillDeletesProduct()
    {
        // Arrange
        var product = CreateFakeProduct();

        _productRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(product);
        _productRepo.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Cache lỗi
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Redis down"));
        _cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new Exception("Redis down"));

        // Act
        var act = () => _sut.DeleteAsync(1);

        // Assert — cache lỗi nhưng delete KHÔNG throw
        await act.Should().NotThrowAsync();
        product.IsDeleted.Should().BeTrue();
    }
}