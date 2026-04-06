using System.Security.Claims;
using Ecommerce.API.Services;
using Ecommerce.Application.Common.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Ecommerce.UnitTests.Services;

public class HttpRequestDeviceContextTests
{
    [Fact]
    public void WhenItemsNotSet_IsDeviceBoundFalse_AndNormalizedDeviceIdNull()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var sut = new HttpRequestDeviceContext(accessor);

        sut.IsDeviceBound.Should().BeFalse();
        sut.NormalizedDeviceId.Should().BeNull();
        sut.UserId.Should().BeNull();
    }

    [Fact]
    public void WhenNameIdentifierClaimPresent_UserIdParsed()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "42") },
            authenticationType: "Bearer"));
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var sut = new HttpRequestDeviceContext(accessor);

        sut.UserId.Should().Be(42);
    }

    [Fact]
    public void WhenDeviceBound_ExposesNormalizedDeviceId()
    {
        var ctx = new DefaultHttpContext();
        ctx.Items[RequestDeviceContextKeys.IsDeviceBound] = true;
        ctx.Items[RequestDeviceContextKeys.NormalizedDeviceId] = "my-device-id";
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var sut = new HttpRequestDeviceContext(accessor);

        sut.IsDeviceBound.Should().BeTrue();
        sut.NormalizedDeviceId.Should().Be("my-device-id");
    }

    [Fact]
    public void WhenHttpContextNull_IsDeviceBoundFalse()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var sut = new HttpRequestDeviceContext(accessor);

        sut.IsDeviceBound.Should().BeFalse();
        sut.NormalizedDeviceId.Should().BeNull();
        sut.UserId.Should().BeNull();
    }
}
