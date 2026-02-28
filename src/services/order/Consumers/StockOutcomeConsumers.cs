using MassTransit;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Messaging;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.Contracts;
using ShopNGo.OrderService.Application;
using ShopNGo.OrderService.Data;
using ShopNGoMessageContext = ShopNGo.BuildingBlocks.Messaging.MessageContext;

namespace ShopNGo.OrderService.Consumers;

public sealed class StockReservedConsumer(
    OrderDbContext db,
    InboxProcessor<OrderDbContext> inbox,
    IOptions<RabbitMqOptions> options,
    OrderApplicationService orderService,
    ILogger<StockReservedConsumer> logger)
    : MassTransitConsumerBase<StockReservedIntegrationEvent, OrderDbContext>(db, inbox, options, logger)
{
    protected override string ConsumerName => nameof(StockReservedConsumer);
    protected override string RoutingKey => EventRoutingKeys.StockReserved;

    protected override Task HandleAsync(
        ShopNGoMessageContext context,
        StockReservedIntegrationEvent message,
        ConsumeContext<StockReservedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        return orderService.ConfirmOrderAsync(message.OrderId, context.CorrelationId, context.TraceParent, ct);
    }
}

public sealed class StockRejectedConsumer(
    OrderDbContext db,
    InboxProcessor<OrderDbContext> inbox,
    IOptions<RabbitMqOptions> options,
    OrderApplicationService orderService,
    ILogger<StockRejectedConsumer> logger)
    : MassTransitConsumerBase<StockRejectedIntegrationEvent, OrderDbContext>(db, inbox, options, logger)
{
    protected override string ConsumerName => nameof(StockRejectedConsumer);
    protected override string RoutingKey => EventRoutingKeys.StockRejected;

    protected override Task HandleAsync(
        ShopNGoMessageContext context,
        StockRejectedIntegrationEvent message,
        ConsumeContext<StockRejectedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        return orderService.RejectOrderAsync(
            message.OrderId,
            message.ReasonCode,
            message.Reason,
            context.CorrelationId,
            context.TraceParent,
            ct);
    }
}

public sealed class OrderOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OrderOutboxDispatcher> logger)
    : OutboxDispatcherBackgroundService<OrderDbContext>(scopeFactory, options, logger);
