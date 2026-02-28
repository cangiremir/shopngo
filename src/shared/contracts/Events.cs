namespace ShopNGo.Contracts;

public static class EventRoutingKeys
{
    public const string OrderCreated = "order.created";
    public const string StockReserved = "stock.reserved";
    public const string StockRejected = "stock.rejected";
    public const string OrderConfirmed = "order.confirmed";
    public const string OrderRejected = "order.rejected";
}

public static class NotificationChannels
{
    public const string Email = "email";
    public const string Sms = "sms";
}

public sealed record OrderItemContract(Guid ProductId, int Quantity);

public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string CustomerEmail,
    IReadOnlyCollection<OrderItemContract> Items,
    DateTimeOffset CreatedAtUtc,
    string NotificationChannel,
    string NotificationTarget);

public sealed record StockReservedIntegrationEvent(Guid OrderId, DateTimeOffset ReservedAtUtc);

public sealed record StockRejectedIntegrationEvent(
    Guid OrderId,
    string ReasonCode,
    string Reason,
    DateTimeOffset RejectedAtUtc);

public sealed record OrderConfirmedIntegrationEvent(
    Guid OrderId,
    string CustomerEmail,
    DateTimeOffset ConfirmedAtUtc,
    string NotificationChannel,
    string NotificationTarget);

public sealed record OrderRejectedIntegrationEvent(
    Guid OrderId,
    string CustomerEmail,
    string ReasonCode,
    string Reason,
    DateTimeOffset RejectedAtUtc,
    string NotificationChannel,
    string NotificationTarget);
