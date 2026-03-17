using Ecommerce.Application.DTOs.Product;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.Application.Interfaces;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _repositoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<ILogger<ProductService>> _loggerMock;

    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _repositoryMock = new Mock<IProductRepository>();
        _cacheMock = new Mock<ICacheService>();
        _loggerMock = new Mock<ILogger<ProductService>>();

        _service = new ProductService(
            _repositoryMock.Object,
            _loggerMock.Object,
            _cacheMock.Object
        );
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnCachedProduct_WhenCacheHit()
    {
        var productId = new System.Random().Next(1, 10000);

        var cachedProduct = new ProductResponseDto
        {
            Id = productId,
            Name = "Laptop"
        };

        _cacheMock
            .Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync(cachedProduct);

        var result = await _service.GetByIdAsync(productId);

        result.Should().BeEquivalentTo(cachedProduct);

        _repositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnProduct_WhenCacheMiss()
    {
        var productId = new System.Random().Next(1, 10000);
        var categoryId = new System.Random().Next(1, 10000);

        var product = new Product
        {
            Id = productId,
            Name = "Laptop",
            Price = 1000,
            CategoryId = categoryId,
            Category = new Category { Id = categoryId, Name = "Electronics" }
        };

        _cacheMock
            .Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(product);

        var result = await _service.GetByIdAsync(productId);

        result.Should().NotBeNull();
        result.Id.Should().Be(productId);

        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<ProductResponseDto>(),
            It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenProductDoesNotExist()
    {
        var productId = new System.Random().Next(1, 10000);

        _cacheMock
            .Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponseDto)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product)null);

        var act = async () => await _service.GetByIdAsync(productId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateProduct_WhenCategoryExists()
    {
        var categoryId = new System.Random().Next(1, 10000);
        var productId = new System.Random().Next(1, 10000);

        var dto = new CreateProductDto
        {
            Name = "Laptop",
            Price = 1000,
            CategoryId = categoryId
        };

        _repositoryMock
            .Setup(r => r.CategoryExistsAsync(dto.CategoryId))
            .ReturnsAsync(true);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Product
            {
                Id = productId,
                Name = dto.Name,
                Price = dto.Price,
                CategoryId = dto.CategoryId,
                Category = new Category { Id = categoryId, Name = "Electronics" }
            });

        var result = await _service.CreateAsync(dto);

        result.Should().NotBeNull();

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Product>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowNotFound_WhenCategoryNotExists()
    {
        var categoryId = new System.Random().Next(1, 10000);

        var dto = new CreateProductDto
        {
            Name = "Laptop",
            Price = 1000,
            CategoryId = categoryId
        };

        _repositoryMock
            .Setup(r => r.CategoryExistsAsync(dto.CategoryId))
            .ReturnsAsync(false);

        var act = async () => await _service.CreateAsync(dto);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateProduct()
    {
        var productId = new System.Random().Next(1, 10000);
        var oldCategoryId = new System.Random().Next(1, 10000);
        var newCategoryId = new System.Random().Next(1, 10000);

        var product = new Product
        {
            Id = productId,
            Name = "Old",
            Price = 500,
            CategoryId = oldCategoryId
        };

        var dto = new UpdateProductDto
        {
            Name = "New",
            Price = 1000,
            CategoryId = newCategoryId
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(product);

        _repositoryMock
            .Setup(r => r.CategoryExistsAsync(dto.CategoryId))
            .ReturnsAsync(true);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(new Product
            {
                Id = productId,
                Name = dto.Name,
                Price = dto.Price,
                CategoryId = dto.CategoryId,
                Category = new Category { Id = newCategoryId, Name = "Electronics" }
            });

        var result = await _service.UpdateAsync(productId, dto);

        result.Name.Should().Be("New");

        _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDeleteProduct()
    {
        var productId = new System.Random().Next(1, 10000);

        var product = new Product
        {
            Id = productId,
            Name = "Laptop"
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(productId))
            .ReturnsAsync(product);

        await _service.DeleteAsync(productId);

        product.IsDeleted.Should().BeTrue();

        _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowNotFound_WhenProductNotExists()
    {
        var productId = new System.Random().Next(1, 10000);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Product)null);

        var act = async () => await _service.DeleteAsync(productId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnCache_WhenCacheHit()
    {
        var query = new ProductQuery
        {
            Page = 1,
            PageSize = 10
        };

        var productId = new System.Random().Next(1, 10000);

        var cached = new PagedResult<ProductResponseDto>(
            new List<ProductResponseDto>
            {
            new ProductResponseDto
            {
                Id = productId,
                Name = "Laptop"
            }
            },
            1,
            10,
            1
        );

        _cacheMock
            .Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(0);

        _cacheMock
            .Setup(c => c.GetAsync<PagedResult<ProductResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync(cached);

        var result = await _service.GetAllAsync(query);

        result.Should().BeEquivalentTo(cached);

        _repositoryMock.Verify(r => r.GetQueryable(), Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnProducts_WhenCacheMiss()
    {
        var query = new ProductQuery
        {
            Page = 1,
            PageSize = 10
        };

        var productId = new System.Random().Next(1, 10000);
        var categoryId = new System.Random().Next(1, 10000);

        var products = new List<Product>
    {
        new Product
        {
            Id = productId,
            Name = "Laptop",
            Price = 1000,
            CategoryId = categoryId,
            Category = new Category { Id = categoryId, Name = "Electronics" }
        }
    }.AsQueryable();

        _cacheMock
            .Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(0);

        _cacheMock
            .Setup(c => c.GetAsync<PagedResult<ProductResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<ProductResponseDto>)null);

        _repositoryMock
            .Setup(r => r.GetQueryable())
            .Returns(products);

        var result = await _service.GetAllAsync(query);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);

        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<PagedResult<ProductResponseDto>>(),
            It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ShouldFilterBySearch()
    {
        var query = new ProductQuery
        {
            Search = "Lap",
            Page = 1,
            PageSize = 10
        };

        var categoryId = new System.Random().Next(1, 10000);
        var laptopId = new System.Random().Next(1, 10000);
        var phoneId = new System.Random().Next(1, 10000);

        var products = new List<Product>
    {
        new Product
        {
            Id = laptopId,
            Name = "Laptop",
            Price = 1000,
            CategoryId = categoryId,
            Category = new Category { Id = categoryId, Name = "Electronics" }
        },
        new Product
        {
            Id = phoneId,
            Name = "Phone",
            Price = 500,
            CategoryId = categoryId,
            Category = new Category { Id = categoryId, Name = "Electronics" }
        }
    }.AsQueryable();

        _cacheMock
            .Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(0);

        _cacheMock
            .Setup(c => c.GetAsync<PagedResult<ProductResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<ProductResponseDto>)null);

        _repositoryMock
            .Setup(r => r.GetQueryable())
            .Returns(products);

        var result = await _service.GetAllAsync(query);

        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAllAsync_ShouldFilterByCategory()
    {
        var electronicsCategoryId = new System.Random().Next(1, 10000);
        var fashionCategoryId = new System.Random().Next(1, 10000);
        var laptopId = new System.Random().Next(1, 10000);
        var shoesId = new System.Random().Next(1, 10000);

        var query = new ProductQuery
        {
            CategoryId = electronicsCategoryId,
            Page = 1,
            PageSize = 10
        };

        var products = new List<Product>
    {
        new Product
        {
            Id = laptopId,
            Name = "Laptop",
            Price = 1000,
            CategoryId = electronicsCategoryId,
            Category = new Category { Id = electronicsCategoryId, Name = "Electronics" }
        },
        new Product
        {
            Id = shoesId,
            Name = "Shoes",
            Price = 100,
            CategoryId = fashionCategoryId,
            Category = new Category { Id = fashionCategoryId, Name = "Fashion" }
        }
    }.AsQueryable();

        _cacheMock
            .Setup(c => c.GetAsync<long?>(It.IsAny<string>()))
            .ReturnsAsync(0);

        _cacheMock
            .Setup(c => c.GetAsync<PagedResult<ProductResponseDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<ProductResponseDto>)null);

        _repositoryMock
            .Setup(r => r.GetQueryable())
            .Returns(products);

        var result = await _service.GetAllAsync(query);

        result.Items.Should().HaveCount(1);
    }
}
