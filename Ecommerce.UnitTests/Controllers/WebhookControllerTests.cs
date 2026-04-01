using System.Text;
using Ecommerce.API.Controllers;
using Ecommerce.Application.Interfaces;
using Ecommerce.UnitTests.Payment;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Ecommerce.UnitTests.Controllers;

public class WebhookControllerTests
{
    private static void SetJsonBody(HttpContext http, string json)
    {
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        http.Request.ContentType = "application/json";
    }

    [Fact]
    public async Task StripeWebhook_WhenSignatureInvalid_ReturnsBadRequest()
    {
        var payment = new TestPaymentWebhookStub { SignatureValid = false };
        var orders = new Mock<IOrderService>(MockBehavior.Strict);
        var http = new DefaultHttpContext();
        SetJsonBody(http, "{}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = await sut.StripeWebhook();

        result.Should().BeOfType<BadRequestObjectResult>();
        orders.Verify(
            o => o.HandlePaymentSucceededAsync(It.IsAny<string>()),
            Times.Never);
        orders.Verify(
            o => o.HandlePaymentFailedAsync(It.IsAny<string>()),
            Times.Never);
        orders.Verify(
            o => o.HandleRefundCompletedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StripeWebhook_WhenPaymentIntentSucceeded_CallsHandlePaymentSucceeded()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "payment_intent.succeeded",
            PaymentIntentIdOut = "pi_test_123"
        };
        var orders = new Mock<IOrderService>();
        var http = new DefaultHttpContext();
        SetJsonBody(http, "{\"id\":\"evt_1\"}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = await sut.StripeWebhook();

        result.Should().BeOfType<OkResult>();
        orders.Verify(o => o.HandlePaymentSucceededAsync("pi_test_123"), Times.Once);
    }

    [Fact]
    public async Task StripeWebhook_WhenChargeSucceeded_CallsHandlePaymentSucceeded()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "charge.succeeded",
            PaymentIntentIdOut = "pi_from_charge"
        };
        var orders = new Mock<IOrderService>();
        var http = new DefaultHttpContext();
        SetJsonBody(http, "{}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await sut.StripeWebhook();

        orders.Verify(o => o.HandlePaymentSucceededAsync("pi_from_charge"), Times.Once);
    }

    [Fact]
    public async Task StripeWebhook_WhenPaymentIntentFailed_CallsHandlePaymentFailed()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "payment_intent.payment_failed",
            PaymentIntentIdOut = "pi_failed"
        };
        var orders = new Mock<IOrderService>();
        var http = new DefaultHttpContext();
        SetJsonBody(http, "{}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await sut.StripeWebhook();

        orders.Verify(o => o.HandlePaymentFailedAsync("pi_failed"), Times.Once);
    }

    [Fact]
    public async Task StripeWebhook_WhenChargeFailed_CallsHandlePaymentFailed()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "charge.failed",
            PaymentIntentIdOut = "pi_cf"
        };
        var orders = new Mock<IOrderService>();
        var http = new DefaultHttpContext();
        SetJsonBody(http, "{}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await sut.StripeWebhook();

        orders.Verify(o => o.HandlePaymentFailedAsync("pi_cf"), Times.Once);
    }

    [Fact]
    public async Task StripeWebhook_WhenSucceededButEmptyPaymentIntentId_DoesNotCallOrderService()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "payment_intent.succeeded",
            PaymentIntentIdOut = string.Empty
        };
        var orders = new Mock<IOrderService>(MockBehavior.Strict);

        var http = new DefaultHttpContext();
        SetJsonBody(http, "{}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = await sut.StripeWebhook();

        result.Should().BeOfType<OkResult>();
        orders.Verify(
            o => o.HandleRefundCompletedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StripeWebhook_WhenChargeRefunded_CallsHandleRefundCompleted()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "charge.refunded",
            PaymentIntentIdOut = "pi_refund_charge"
        };
        var orders = new Mock<IOrderService>();
        var http = new DefaultHttpContext();
        var payload =
            """{"data":{"object":{"refunds":{"data":[{"id":"re_from_charge"}]}},"id":"evt"}}""";
        SetJsonBody(http, payload);
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await sut.StripeWebhook();

        orders.Verify(
            o => o.HandleRefundCompletedAsync("pi_refund_charge", "re_from_charge", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StripeWebhook_WhenRefundUpdatedNotSucceeded_DoesNotCallHandleRefundCompleted()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "refund.updated",
            PaymentIntentIdOut = "pi_ru"
        };
        var orders = new Mock<IOrderService>(MockBehavior.Strict);
        var http = new DefaultHttpContext();
        SetJsonBody(http, """{"data":{"object":{"id":"re_x","status":"pending"}}}""");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = await sut.StripeWebhook();

        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task StripeWebhook_WhenRefundUpdatedSucceeded_CallsHandleRefundCompleted()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "refund.updated",
            PaymentIntentIdOut = "pi_ru_ok"
        };
        var orders = new Mock<IOrderService>();
        var http = new DefaultHttpContext();
        SetJsonBody(http, """{"data":{"object":{"id":"re_ok","status":"succeeded"}}}""");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await sut.StripeWebhook();

        orders.Verify(
            o => o.HandleRefundCompletedAsync("pi_ru_ok", "re_ok", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StripeWebhook_WhenUnknownEventType_ReturnsOkWithoutCallingHandlers()
    {
        var payment = new TestPaymentWebhookStub
        {
            SignatureValid = true,
            EventTypeOut = "customer.created",
            PaymentIntentIdOut = "pi_x"
        };
        var orders = new Mock<IOrderService>(MockBehavior.Strict);

        var http = new DefaultHttpContext();
        SetJsonBody(http, "{}");
        var sut = new WebhookController(payment, orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = await sut.StripeWebhook();

        result.Should().BeOfType<OkResult>();
    }
}
