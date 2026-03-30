using System.IO;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhookController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IOrderService _orderService;

    public WebhookController(IPaymentService paymentService, IOrderService orderService)
    {
        _paymentService = paymentService;
        _orderService = orderService;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        string payload;
        using (var reader = new StreamReader(Request.Body))
            payload = await reader.ReadToEndAsync();

        var signature = Request.Headers["Stripe-Signature"].ToString();

        var isValid = await _paymentService.ValidateWebhookSignature(
            payload, signature, out var eventType, out var paymentIntentId);

        if (!isValid)
            return BadRequest("Invalid signature");

        switch (eventType)
        {
            case "payment_intent.succeeded":
            case "charge.succeeded":
                if (!string.IsNullOrEmpty(paymentIntentId))
                    await _orderService.HandlePaymentSucceededAsync(paymentIntentId);
                break;

            case "payment_intent.payment_failed":
                if (!string.IsNullOrEmpty(paymentIntentId))
                    await _orderService.HandlePaymentFailedAsync(paymentIntentId);
                break;

            case "charge.failed":
                if (!string.IsNullOrEmpty(paymentIntentId))
                    await _orderService.HandlePaymentFailedAsync(paymentIntentId);
                break;
        }

        return Ok();
    }
}