using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Stripe;

namespace Ecommerce.Infrastructure.Payment;

public class StripePaymentService : IPaymentService
{
    private readonly StripeSettings _settings;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(IOptions<StripeSettings> settings, ILogger<StripePaymentService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<PaymentIntentCreateResult> CreatePaymentIntentAsync(
        long amountInCents,
        string currency,
        string orderId,
        string idempotencyKey)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = amountInCents,
            Currency = currency,
            Metadata = new Dictionary<string, string>
            {
                { "orderId", orderId }
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never"
            }
        };

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = idempotencyKey
        };

        var service = new PaymentIntentService();
        var paymentIntent = await service.CreateAsync(options, requestOptions);

        _logger.LogInformation("Created PaymentIntent {Id} for Order {OrderId}",
            paymentIntent.Id, orderId);

        return new PaymentIntentCreateResult(
            paymentIntent.ClientSecret ?? string.Empty,
            paymentIntent.Id);
    }

    public async Task<PaymentIntentCreateResult?> GetReusablePaymentIntentAsync(
        string paymentIntentId,
        long expectedAmountCents,
        string currency)
    {
        var service = new PaymentIntentService();
        PaymentIntent pi;
        try
        {
            pi = await service.GetAsync(paymentIntentId);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Could not retrieve PaymentIntent {PaymentIntentId}", paymentIntentId);
            return null;
        }

        if (!string.Equals(pi.Currency, currency, StringComparison.OrdinalIgnoreCase))
            return null;
        if (pi.Amount != expectedAmountCents)
            return null;

        if (pi.Status is "requires_payment_method" or "requires_confirmation" or "requires_action")
        {
            return new PaymentIntentCreateResult(
                pi.ClientSecret ?? string.Empty,
                pi.Id);
        }

        return null;
    }

    public Task<bool> ValidateWebhookSignature(
        string payload, string? signature,
        out string eventType, out string paymentIntentId)
    {
        eventType = string.Empty;
        paymentIntentId = string.Empty;

        if (string.IsNullOrEmpty(signature))
            return Task.FromResult(false);

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                payload, signature, _settings.WebhookSecret);

            eventType = stripeEvent.Type ?? string.Empty;

            var obj = stripeEvent.Data?.Object;
            if (obj is PaymentIntent pi)
                paymentIntentId = pi.Id ?? string.Empty;
            else if (obj is Charge charge && !string.IsNullOrEmpty(charge.PaymentIntentId))
                paymentIntentId = charge.PaymentIntentId;

            if (string.IsNullOrEmpty(paymentIntentId))
            {
                if (eventType.StartsWith("payment_intent.", StringComparison.Ordinal))
                    paymentIntentId = TryParsePaymentIntentIdFromPayload(payload);
                else if (eventType is "charge.succeeded" or "charge.failed")
                    paymentIntentId = TryParsePaymentIntentIdFromChargePayload(payload);
            }

            if (string.IsNullOrEmpty(paymentIntentId) &&
                (eventType.StartsWith("payment_intent.", StringComparison.Ordinal) ||
                 eventType is "charge.succeeded" or "charge.failed"))
            {
                _logger.LogWarning(
                    "Stripe webhook {EventType}: signature OK but PaymentIntent id missing; DB will not update for this event.",
                    eventType);
            }

            return Task.FromResult(true);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Webhook signature validation failed: {Message}", ex.Message);
            return Task.FromResult(false);
        }
    }

    private static string TryParsePaymentIntentIdFromPayload(string payload)
    {
        try
        {
            var root = JObject.Parse(payload);
            return root["data"]?["object"]?["id"]?.Value<string>() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryParsePaymentIntentIdFromChargePayload(string payload)
    {
        try
        {
            var token = JObject.Parse(payload)["data"]?["object"]?["payment_intent"];
            if (token == null || token.Type == JTokenType.Null)
                return string.Empty;
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? string.Empty;
            return token["id"]?.Value<string>() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}