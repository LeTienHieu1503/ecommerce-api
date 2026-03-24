using System.Security.Claims;
using Ecommerce.Application.DTOs.Order;
using Ecommerce.Application.Interfaces;
using Ecommerce.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController : BaseApiController
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [Authorize(Policy = "order.create")]
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
        {
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));
        }

        request.UserId = userId;

        var order = await _orderService.CreateOrderAsync(request);
        return CreatedSuccess(order, "Order created successfully");
    }

    [Authorize(Policy = "order.read")]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null)
            return NotFound(new ApiResponse<string>(404, false, "Order not found", null));

        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!User.IsInRole("Admin"))
        {
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int currentUserId) || order.UserId != currentUserId)
            {
                return Forbid();
            }
        }

        return Success(order);
    }

    [Authorize(Policy = Authorization.Policies.AuthorizationPolicies.AdminOnly)]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] OrderQuery query)
    {
        var result = await _orderService.GetAllOrdersAsync(query);
        return Success(result);
    }

    [Authorize(Policy = "order.read")]
    [HttpGet("user/{userId:int}")]
    public async Task<IActionResult> GetByUser(int userId)
    {
        if (!User.IsInRole("Admin"))
        {
            var currentUserIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserIdString) || !int.TryParse(currentUserIdString, out int currentUserId) || currentUserId != userId)
            {
                return Forbid();
            }
        }

        var orders = await _orderService.GetOrdersByUserIdAsync(userId);
        return Success(orders);
    }

    [Authorize(Policy = "order.delete")]
    [HttpPut("{id:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int currentUserId))
        {
            return Unauthorized(new ApiResponse<string>(401, false, "Invalid user identifier", null));
        }

        var canCancelAnyOrder = User.IsInRole("Admin");
        await _orderService.CancelOrderAsync(id, currentUserId, canCancelAnyOrder);

        var order = await _orderService.GetOrderByIdAsync(id);
        return Success(order, "Order cancelled successfully");
    }
}