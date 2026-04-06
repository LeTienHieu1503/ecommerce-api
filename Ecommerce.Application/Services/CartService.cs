using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Domain.Exceptions;
using Ecommerce.Application.DTOs.Cart;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Ecommerce.Application.Services;

public class CartService : ICartService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<CartService> _logger;
    private const int CartTtlDays = 7;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CartService(
        IConnectionMultiplexer redis,
        IProductRepository productRepository,
        ILogger<CartService> logger)
    {
        _redis = redis;
        _productRepository = productRepository;
        _logger = logger;
    }

    private string GetCartKey(int userId) => $"cart:{userId}";

    private async Task<Cart> GetOrCreateCartAsync(int userId)
    {
        var db = _redis.GetDatabase();
        var key = GetCartKey(userId);
        var data = await db.StringGetAsync(key);

        if (data.IsNullOrEmpty)
            return new Cart { UserId = userId };

        var cart = JsonSerializer.Deserialize<Cart>(data!, JsonOptions)
                   ?? new Cart { UserId = userId };
        if (cart.UserId == 0)
            cart.UserId = userId;
        return cart;
    }

    private async Task SaveCartAsync(Cart cart)
    {
        var db = _redis.GetDatabase();
        var key = GetCartKey(cart.UserId);
        cart.LastUpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(cart, JsonOptions);
        await db.StringSetAsync(key, json, TimeSpan.FromDays(CartTtlDays));
    }

    public async Task<CartDto> GetCartAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var dto = MapToDto(cart);
        if (dto.LastUpdatedAt == default)
            dto.LastUpdatedAt = DateTime.UtcNow;
        return dto;
    }

    public async Task<CartDto> AddToCartAsync(int userId, AddToCartRequest request)
    {
        // Bước 1: Lấy product từ DB để validate
        var product = await _productRepository.GetByIdAsync(request.ProductId)
            ?? throw new NotFoundException($"Product {request.ProductId} not found");

        // Bước 2: Lấy cart hiện tại
        var cart = await GetOrCreateCartAsync(userId);

        // Bước 3: Kiểm tra item đã có chưa
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);

        if (existingItem != null)
        {
            var newQty = existingItem.Quantity + request.Quantity;
            if (product.Stock < newQty)
                throw new BusinessException($"Only {product.Stock} items available in stock");

            existingItem.Quantity = newQty;
        }
        else
        {
            if (product.Stock < request.Quantity)
                throw new BusinessException($"Only {product.Stock} items available in stock");

            cart.Items.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = request.Quantity
            });
        }

        // Bước 4: Lưu cart
        await SaveCartAsync(cart);

        _logger.LogInformation("User {UserId} added product {ProductId} to cart", userId, request.ProductId);
        return MapToDto(cart);
    }

    public async Task<CartDto> UpdateCartItemAsync(int userId, int productId, UpdateCartItemRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new NotFoundException($"Product {productId} not found in cart");

        if (request.Quantity == 0)
        {
            cart.Items.Remove(item);
        }
        else
        {
            var product = await _productRepository.GetByIdAsync(productId)
                ?? throw new NotFoundException($"Product {productId} not found");

            if (product.Stock < request.Quantity)
                throw new BusinessException($"Only {product.Stock} items available in stock");

            item.Quantity = request.Quantity;
            item.UnitPrice = product.Price;
        }

        await SaveCartAsync(cart);
        return MapToDto(cart);
    }

    public async Task RemoveCartItemAsync(int userId, int productId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            cart.Items.Remove(item);
            await SaveCartAsync(cart);
        }
    }

    public async Task ClearCartAsync(int userId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(GetCartKey(userId));
    }

    private static CartDto MapToDto(Cart cart) => new()
    {
        UserId = cart.UserId,
        LastUpdatedAt = cart.LastUpdatedAt,
        TotalAmount = cart.TotalAmount,
        Items = cart.Items.Select(i => new CartItemDto
        {
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            TotalPrice = i.TotalPrice
        }).ToList()
    };
}