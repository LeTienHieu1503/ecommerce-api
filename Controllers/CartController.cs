using Ecommerce.Application.DTOs.Cart;
using Ecommerce.Application.Interfaces;
using Ecommerce.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : BaseApiController
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        var cart = await _cartService.GetCartAsync(userId.Value);
        return Success(cart);
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        var cart = await _cartService.AddToCartAsync(userId.Value, request);
        return Success(cart, "Item added to cart");
    }

    [HttpPut("items/{productId:int}")]
    public async Task<IActionResult> UpdateCartItem(
        int productId,
        [FromBody] UpdateCartItemRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        var cart = await _cartService.UpdateCartItemAsync(userId.Value, productId, request);
        return Success(cart, "Cart updated");
    }

    [HttpDelete("items/{productId:int}")]
    public async Task<IActionResult> RemoveCartItem(int productId)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        await _cartService.RemoveCartItemAsync(userId.Value, productId);
        return DeleteSuccess("Item removed from cart");
    }

    [HttpDelete]
    public async Task<IActionResult> ClearCart()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));

        await _cartService.ClearCartAsync(userId.Value);
        return DeleteSuccess("Cart cleared");
    }
}
