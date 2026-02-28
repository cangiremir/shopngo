using MassTransit;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.BuildingBlocks.Messaging;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.Contracts;
using ShopNGo.NotificationService.Application;
using ShopNGo.NotificationService.Data;
using ShopNGoMessageContext = ShopNGo.BuildingBlocks.Messaging.MessageContext;

namespace ShopNGo.NotificationService.Consumers;

public sealed class OrderConfirmedNotificationConsumer(
    NotificationDbContext db,
    InboxProcessor<NotificationDbContext> inbox,
    IOptions<RabbitMqOptions> options,
    NotificationDispatchService dispatcher,
    ILogger<OrderConfirmedNotificationConsumer> logger)
    : MassTransitConsumerBase<OrderConfirmedIntegrationEvent, NotificationDbContext>(db, inbox, options, logger)
{
    protected override string ConsumerName => nameof(OrderConfirmedNotificationConsumer);
    protected override string RoutingKey => EventRoutingKeys.OrderConfirmed;

    protected override async Task HandleAsync(
        ShopNGoMessageContext context,
        OrderConfirmedIntegrationEvent message,
        ConsumeContext<OrderConfirmedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        var log = dispatcher.Dispatch(message.ToDispatchRequest());

        Db.NotificationLogs.Add(log);
        await Db.SaveChangesAsync(ct);
        logger.LogInformation("Stored order confirmation notification for {OrderId}", message.OrderId);
    }

    protected override async Task OnBusinessExceptionAsync(
        ShopNGoMessageContext context,
        OrderConfirmedIntegrationEvent message,
        BusinessRuleException ex,
        ConsumeContext<OrderConfirmedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        Db.NotificationLogs.Add(message.ToFailedLog(ex.ErrorCode));
        await Db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Stored failed notification record for order.confirmed due to {ErrorCode}; orderId={OrderId} channel={Channel} target={Target}",
            ex.ErrorCode,
            message.OrderId,
            message.NotificationChannel,
            message.NotificationTarget);
    }
}

public sealed class OrderRejectedNotificationConsumer(
    NotificationDbContext db,
    InboxProcessor<NotificationDbContext> inbox,
    IOptions<RabbitMqOptions> options,
    NotificationDispatchService dispatcher,
    ILogger<OrderRejectedNotificationConsumer> logger)
    : MassTransitConsumerBase<OrderRejectedIntegrationEvent, NotificationDbContext>(db, inbox, options, logger)
{
    protected override string ConsumerName => nameof(OrderRejectedNotificationConsumer);
    protected override string RoutingKey => EventRoutingKeys.OrderRejected;

    protected override async Task HandleAsync(
        ShopNGoMessageContext context,
        OrderRejectedIntegrationEvent message,
        ConsumeContext<OrderRejectedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        var log = dispatcher.Dispatch(message.ToDispatchRequest());

        Db.NotificationLogs.Add(log);
        await Db.SaveChangesAsync(ct);
        logger.LogInformation("Stored order rejection notification for {OrderId}", message.OrderId);
    }

    protected override async Task OnBusinessExceptionAsync(
        ShopNGoMessageContext context,
        OrderRejectedIntegrationEvent message,
        BusinessRuleException ex,
        ConsumeContext<OrderRejectedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        Db.NotificationLogs.Add(message.ToFailedLog(ex.ErrorCode));
        await Db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Stored failed notification record for order.rejected due to {ErrorCode}; orderId={OrderId} channel={Channel} target={Target}",
            ex.ErrorCode,
            message.OrderId,
            message.NotificationChannel,
            message.NotificationTarget);
    }
}
