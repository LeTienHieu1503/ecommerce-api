using Ecommerce.Application.DTOs.Cart;

namespace Ecommerce.Application.Interfaces;

public interface ICartService
{
    Task<CartDto> GetCartAsync(int userId);
    Task<CartDto> AddToCartAsync(int userId, AddToCartRequest request);
    Task<CartDto> UpdateCartItemAsync(int userId, int productId, UpdateCartItemRequest request);
    Task RemoveCartItemAsync(int userId, int productId);
    Task ClearCartAsync(int userId);
}