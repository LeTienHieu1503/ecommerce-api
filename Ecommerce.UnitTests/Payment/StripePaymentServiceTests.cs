using System.Security.Cryptography;
using System.Text;
using Ecommerce.Infrastructure.Payment;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ecommerce.UnitTests.Payment;

public class StripePaymentServiceTests
{
    private static StripePaymentService CreateSut(string webhookSecret)
    {
        var settings = new StripeSettings
        {
            SecretKey = "sk_test_unit",
            WebhookSecret = webhookSecret
        };
        return new StripePaymentService(
            Options.Create(settings),
            NullLogger<StripePaymentService>.Instance);
    }

    /// <summary>Matches Stripe.net EventUtility.ComputeSignature (UTF-8 secret + payload).</summary>
    private static string BuildStripeSignatureHeader(string secret, long unixTimestamp, string payload)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var secretBytes = utf8.GetBytes(secret);
        var payloadBytes = utf8.GetBytes($"{unixTimestamp}.{payload}");
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return $"t={unixTimestamp},v1={hex}";
    }

    [Fact]
    public async Task ValidateWebhookSignature_WhenSignatureHeaderEmpty_ReturnsFalse()
    {
        var sut = CreateSut("whsec_test");

        var ok = await sut.ValidateWebhookSignature("{}", string.Empty, out var eventType, out var paymentIntentId);

        ok.Should().BeFalse();
        eventType.Should().BeEmpty();
        paymentIntentId.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateWebhookSignature_WhenSignatureHeaderNull_ReturnsFalse()
    {
        var sut = CreateSut("whsec_test");

        var ok = await sut.ValidateWebhookSignature("{}", null!, out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateWebhookSignature_WhenHmacWrong_ReturnsFalse()
    {
        var secret = "whsec_unit_test_secret_value";
        var sut = CreateSut(secret);
        var payload = "{\"id\":\"evt_x\",\"object\":\"event\"}";
        var header = BuildStripeSignatureHeader("different_secret", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), payload);

        var ok = await sut.ValidateWebhookSignature(payload, header, out _, out _);

        ok.Should().BeFalse();
    }
}
