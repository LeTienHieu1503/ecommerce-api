using Ecommerce.Application.Interfaces;

namespace Ecommerce.UnitTests.Payment;

/// <summary>Stub for webhook tests: only ValidateWebhookSignature is implemented.</summary>
internal sealed class TestPaymentWebhookStub : IPaymentService
{
    public bool SignatureValid { get; init; } = true;
    public string EventTypeOut { get; init; } = string.Empty;
    public string PaymentIntentIdOut { get; init; } = string.Empty;

    public Task<bool> ValidateWebhookSignature(
        string payload, string? signature, out string eventType, out string paymentIntentId)
    {
        eventType = EventTypeOut;
        paymentIntentId = PaymentIntentIdOut;
        return Task.FromResult(SignatureValid);
    }

    public Task<PaymentIntentCreateResult> CreatePaymentIntentAsync(
        long amountInCents, string currency, string orderId, string idempotencyKey) =>
        throw new NotSupportedException();

    public Task<PaymentIntentCreateResult?> GetReusablePaymentIntentAsync(
        string paymentIntentId, long expectedAmountCents, string currency) =>
        throw new NotSupportedException();
}
