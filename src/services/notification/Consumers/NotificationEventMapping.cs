using System.Text.Json;
using ShopNGo.BuildingBlocks.Serialization;
using ShopNGo.Contracts;
using ShopNGo.NotificationService.Application;
using ShopNGo.NotificationService.Domain;

namespace ShopNGo.NotificationService.Consumers;

internal static class NotificationEventMapping
{
    private const string OrderConfirmedTemplate = "order-confirmed";
    private const string OrderRejectedTemplate = "order-rejected";

    public static NotificationDispatchRequest ToDispatchRequest(this OrderConfirmedIntegrationEvent message)
        => new(
            message.OrderId,
            message.NotificationChannel,
            message.NotificationTarget,
            OrderConfirmedTemplate,
            Serialize(message));

    public static NotificationDispatchRequest ToDispatchRequest(this OrderRejectedIntegrationEvent message)
        => new(
            message.OrderId,
            message.NotificationChannel,
            message.NotificationTarget,
            OrderRejectedTemplate,
            Serialize(message));

    public static NotificationLog ToFailedLog(this OrderConfirmedIntegrationEvent message, string errorCode)
        => NotificationLog.Failed(
            message.OrderId,
            message.NotificationTarget,
            message.NotificationChannel,
            OrderConfirmedTemplate,
            Serialize(message),
            errorCode);

    public static NotificationLog ToFailedLog(this OrderRejectedIntegrationEvent message, string errorCode)
        => NotificationLog.Failed(
            message.OrderId,
            message.NotificationTarget,
            message.NotificationChannel,
            OrderRejectedTemplate,
            Serialize(message),
            errorCode);

    private static string Serialize<T>(T message)
        => JsonSerializer.Serialize(message, JsonDefaults.Options);
}
