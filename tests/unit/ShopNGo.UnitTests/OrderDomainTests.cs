using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using ShopNGo.OrderService.Domain;

namespace ShopNGo.UnitTests;

public sealed class OrderDomainTests
{
    [Fact]
    public void Create_WithValidItems_SetsPendingStock()
    {
        var order = Order.Create(
            "user@example.com",
            customerPhone: null,
            notificationChannel: null,
            [(Guid.NewGuid(), 2), (Guid.NewGuid(), 1)]);

        Assert.Equal(OrderStatus.PendingStock, order.Status);
        Assert.Equal("user@example.com", order.CustomerEmail);
        Assert.Equal(NotificationChannels.Email, order.NotificationChannel);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public void Create_WithoutItems_ThrowsBusinessRuleException()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Order.Create("user@example.com", null, null, []));

        Assert.Equal(ErrorCodes.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public void MarkConfirmed_AfterRejected_ThrowsInvalidState()
    {
        var order = Order.Create("user@example.com", null, null, [(Guid.NewGuid(), 1)]);
        order.MarkRejected(ErrorCodes.InsufficientStock, "Not enough stock.");

        var ex = Assert.Throws<BusinessRuleException>(() => order.MarkConfirmed());
        Assert.Equal(ErrorCodes.OrderInvalidState, ex.ErrorCode);
    }

    [Fact]
    public void MarkRejected_SetsReason()
    {
        var order = Order.Create("user@example.com", null, null, [(Guid.NewGuid(), 1)]);

        order.MarkRejected(ErrorCodes.InsufficientStock, "Insufficient stock.");

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(ErrorCodes.InsufficientStock, order.RejectionReasonCode);
        Assert.Equal("Insufficient stock.", order.RejectionReason);
    }

    [Fact]
    public void Create_WithSmsChannel_UsesPhoneAsNotificationTarget()
    {
        var phone = "+15551234567";
        var order = Order.Create("user@example.com", phone, NotificationChannels.Sms, [(Guid.NewGuid(), 1)]);

        Assert.Equal(NotificationChannels.Sms, order.NotificationChannel);
        Assert.Equal(phone, order.CustomerPhone);
        Assert.Equal(phone, order.GetNotificationTarget());
    }

    [Fact]
    public void Create_WithSmsChannelAndMissingPhone_ThrowsBusinessRuleException()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Order.Create("user@example.com", null, NotificationChannels.Sms, [(Guid.NewGuid(), 1)]));

        Assert.Equal(ErrorCodes.InvalidRequest, ex.ErrorCode);
    }
}
