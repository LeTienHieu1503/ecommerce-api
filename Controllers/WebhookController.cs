using System.IO;
using System.Text.Json;
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

            case "charge.refunded":
                if (!string.IsNullOrEmpty(paymentIntentId)
                    && IsRefundWebhookSucceeded(payload, eventType))
                {
                    await _orderService.HandleRefundCompletedAsync(
                        paymentIntentId,
                        TryGetStripeRefundIdFromPayload(payload, eventType),
                        HttpContext.RequestAborted);
                }

                break;

            case "refund.updated":
                if (!string.IsNullOrEmpty(paymentIntentId)
                    && IsRefundWebhookSucceeded(payload, eventType))
                {
                    await _orderService.HandleRefundCompletedAsync(
                        paymentIntentId,
                        TryGetStripeRefundIdFromPayload(payload, eventType),
                        HttpContext.RequestAborted);
                }

                break;
        }

        return Ok();
    }

    private static bool IsRefundWebhookSucceeded(string payload, string eventType)
    {
        if (eventType == "charge.refunded")
            return true;

        if (eventType != "refund.updated")
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var status = doc.RootElement
                .GetProperty("data")
                .GetProperty("object")
                .GetProperty("status")
                .GetString();
            return string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetStripeRefundIdFromPayload(string payload, string eventType)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var obj = doc.RootElement.GetProperty("data").GetProperty("object");

            if (eventType == "charge.refunded"
                && obj.TryGetProperty("refunds", out var refunds)
                && refunds.TryGetProperty("data", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0
                && arr[0].TryGetProperty("id", out var firstRefundId))
                return firstRefundId.GetString();

            return obj.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}