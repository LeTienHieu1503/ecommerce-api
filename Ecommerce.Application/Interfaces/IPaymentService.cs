namespace Ecommerce.Application.Interfaces;

public sealed record PaymentIntentCreateResult(string ClientSecret, string PaymentIntentId);

public interface IPaymentService
{
    Task<PaymentIntentCreateResult> CreatePaymentIntentAsync(
        long amountInCents,
        string currency,
        string orderId,
        string idempotencyKey);

    /// <summary>If the intent is still payable with the same amount/currency, returns its client secret; otherwise null.</summary>
    Task<PaymentIntentCreateResult?> GetReusablePaymentIntentAsync(
        string paymentIntentId,
        long expectedAmountCents,
        string currency);

    Task<bool> ValidateWebhookSignature(string payload, string? signature, out string eventType, out string paymentIntentId);
}