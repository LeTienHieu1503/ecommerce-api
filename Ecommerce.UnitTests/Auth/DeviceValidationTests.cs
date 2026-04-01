using Ecommerce.Application.Common.Security;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Services;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Ecommerce.UnitTests.Auth;

public class DeviceValidationTests
{
    private const string Secret = "unit-test-device-secret";

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthSecurity:DeviceBindingSecret"] = Secret
            })
            .Build();

    private static User CreateUser(string? lastDeviceHash = null) =>
        new()
        {
            Id = 1,
            Email = "u@test.com",
            PasswordHash = "x",
            CurrentSessionId = "sess1",
            LastLoginDeviceHash = lastDeviceHash
        };

    [Fact]
    public async Task ValidateAsync_EmptyHeader_ThrowsMissingHeader()
    {
        var deviceSession = new Mock<IDeviceSessionService>();
        deviceSession.Setup(s => s.IsSessionActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new DeviceBindingValidationService(CreateConfig(), deviceSession.Object);
        var session = new UserSessionState
        {
            SessionId = "s",
            SessionVersion = 1,
            IpHash = "ip",
            DeviceBindingHash = "hash"
        };

        var act = () => sut.ValidateAsync(null, "hash", true, session, CreateUser("hash"));

        await act.Should().ThrowAsync<DeviceValidationException>()
            .Where(e => e.Reason == DeviceValidationResult.MissingHeader);
    }

    [Fact]
    public async Task ValidateAsync_HeaderHashMismatchJwt_ThrowsDeviceMismatch()
    {
        var deviceSession = new Mock<IDeviceSessionService>();
        deviceSession.Setup(s => s.IsSessionActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new DeviceBindingValidationService(CreateConfig(), deviceSession.Object);
        var jwtHash = DeviceBindingHelper.ComputeDeviceHash("good-device", Secret);
        var session = new UserSessionState
        {
            SessionId = "s",
            SessionVersion = 1,
            IpHash = "ip",
            DeviceBindingHash = jwtHash
        };

        var act = () => sut.ValidateAsync("wrong-device", jwtHash, true, session, CreateUser(jwtHash));

        await act.Should().ThrowAsync<DeviceValidationException>()
            .Where(e => e.Reason == DeviceValidationResult.DeviceMismatch);
    }

    [Fact]
    public async Task ValidateAsync_NotFromRedis_UserHasNoDeviceHashButJwtHasDbh_ThrowsSessionRevoked()
    {
        var deviceSession = new Mock<IDeviceSessionService>();
        deviceSession.Setup(s => s.IsSessionActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new DeviceBindingValidationService(CreateConfig(), deviceSession.Object);
        var jwtHash = DeviceBindingHelper.ComputeDeviceHash("dev", Secret);
        var session = new UserSessionState
        {
            SessionId = "s",
            SessionVersion = 1,
            IpHash = "ip",
            DeviceBindingHash = jwtHash
        };
        var user = CreateUser(lastDeviceHash: null);

        var act = () => sut.ValidateAsync("dev", jwtHash, false, session, user);

        await act.Should().ThrowAsync<DeviceValidationException>()
            .Where(e => e.Reason == DeviceValidationResult.SessionRevoked);
    }

    [Fact]
    public async Task ValidateAsync_NotFromRedis_UserDbHashDiffersFromJwt_ThrowsSessionRotated()
    {
        var deviceSession = new Mock<IDeviceSessionService>();
        deviceSession.Setup(s => s.IsSessionActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new DeviceBindingValidationService(CreateConfig(), deviceSession.Object);
        var jwtHash = DeviceBindingHelper.ComputeDeviceHash("dev", Secret);
        var staleHash = DeviceBindingHelper.ComputeDeviceHash("old-device", Secret);
        var session = new UserSessionState
        {
            SessionId = "s",
            SessionVersion = 1,
            IpHash = "ip",
            DeviceBindingHash = jwtHash
        };
        var user = CreateUser(lastDeviceHash: staleHash);

        var act = () => sut.ValidateAsync("dev", jwtHash, false, session, user);

        await act.Should().ThrowAsync<DeviceValidationException>()
            .Where(e => e.Reason == DeviceValidationResult.SessionRotated);
    }

    [Fact]
    public async Task ValidateAsync_FromRedis_HashMatchesJwt_Completes()
    {
        var deviceSession = new Mock<IDeviceSessionService>();
        deviceSession.Setup(s => s.IsSessionActiveAsync("sid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new DeviceBindingValidationService(CreateConfig(), deviceSession.Object);
        var jwtHash = DeviceBindingHelper.ComputeDeviceHash("my-device", Secret);
        var session = new UserSessionState
        {
            SessionId = "sid-1",
            SessionVersion = 1,
            IpHash = "ip",
            DeviceBindingHash = jwtHash
        };
        var user = CreateUser(jwtHash);

        await sut.ValidateAsync("my-device", jwtHash, true, session, user);

        deviceSession.Verify(
            s => s.IsSessionActiveAsync("sid-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
