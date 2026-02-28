using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using ShopNGo.NotificationService.Domain;

namespace ShopNGo.UnitTests;

public sealed class NotificationLogTests
{
    [Fact]
    public void Create_WithValidEmail_ReturnsSentLog()
    {
        var log = NotificationLog.Create(Guid.NewGuid(), "user@example.com", "order-confirmed", "{}");

        Assert.Equal("Sent", log.Status);
        Assert.Equal("email", log.Channel);
        Assert.Null(log.ErrorCode);
    }

    [Fact]
    public void Create_WithInvalidTarget_ThrowsBusinessRuleException()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NotificationLog.Create(Guid.NewGuid(), "invalid-target", "order-confirmed", "{}"));

        Assert.Equal(ErrorCodes.NotificationInvalidTarget, ex.ErrorCode);
    }

    [Fact]
    public void Create_WithValidSmsTarget_ReturnsSentLog()
    {
        var log = NotificationLog.Create(Guid.NewGuid(), "+15551234567", NotificationChannels.Sms, "order-confirmed", "{}");

        Assert.Equal("Sent", log.Status);
        Assert.Equal(NotificationChannels.Sms, log.Channel);
        Assert.Null(log.ErrorCode);
    }

    [Fact]
    public void Create_WithInvalidChannel_ThrowsBusinessRuleException()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NotificationLog.Create(Guid.NewGuid(), "user@example.com", "fax", "order-confirmed", "{}"));

        Assert.Equal(ErrorCodes.NotificationInvalidChannel, ex.ErrorCode);
    }

    [Fact]
    public void Failed_CreatesRejectedLog()
    {
        var log = NotificationLog.Failed(Guid.NewGuid(), "user@example.com", "order-rejected", "{}", ErrorCodes.NotificationInvalidTarget);

        Assert.Equal("Rejected", log.Status);
        Assert.Equal(ErrorCodes.NotificationInvalidTarget, log.ErrorCode);
    }
}
