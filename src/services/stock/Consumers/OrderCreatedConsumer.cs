using MassTransit;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Messaging;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.Contracts;
using ShopNGo.StockService.Application;
using ShopNGo.StockService.Data;
using ShopNGoMessageContext = ShopNGo.BuildingBlocks.Messaging.MessageContext;

namespace ShopNGo.StockService.Consumers;

public sealed class OrderCreatedConsumer(
    StockDbContext db,
    InboxProcessor<StockDbContext> inbox,
    IOptions<RabbitMqOptions> options,
    StockApplicationService stockService,
    ILogger<OrderCreatedConsumer> logger)
    : MassTransitConsumerBase<OrderCreatedIntegrationEvent, StockDbContext>(db, inbox, options, logger)
{
    protected override string ConsumerName => nameof(OrderCreatedConsumer);
    protected override string RoutingKey => EventRoutingKeys.OrderCreated;

    protected override Task HandleAsync(
        ShopNGoMessageContext context,
        OrderCreatedIntegrationEvent message,
        ConsumeContext<OrderCreatedIntegrationEvent> consumeContext,
        CancellationToken ct)
    {
        return stockService.HandleOrderCreatedAsync(message, context.CorrelationId, context.TraceParent, ct);
    }
}

public sealed class StockOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<StockOutboxDispatcher> logger)
    : OutboxDispatcherBackgroundService<StockDbContext>(scopeFactory, options, logger);
