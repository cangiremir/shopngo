using System.Text.Json;
using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Serialization;
using ShopNGo.Contracts;

namespace ShopNGo.BuildingBlocks.Messaging;

public sealed class MassTransitEventPublisher(
    ISendEndpointProvider sendEndpointProvider,
    IOptions<RabbitMqOptions> options,
    ILogger<MassTransitEventPublisher> logger) : IIntegrationEventPublisher
{
    private readonly RabbitMqOptions _options = options.Value;

    public Task PublishAsync<T>(string routingKey, T message, MessageMetadata metadata, CancellationToken ct = default)
        => SendInternalAsync(routingKey, message!, typeof(T), metadata, ct);

    public async Task PublishRawAsync(string routingKey, string payloadJson, MessageMetadata metadata, CancellationToken ct = default)
    {
        var message = DeserializeByRoutingKey(routingKey, payloadJson)
                      ?? throw new InvalidOperationException($"Cannot deserialize payload for routing key '{routingKey}'.");

        await SendInternalAsync(routingKey, message, message.GetType(), metadata, ct);
    }

    private async Task SendInternalAsync(string routingKey, object payload, Type payloadType, MessageMetadata metadata, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var publishActivity = MessagingDiagnostics.StartProducerActivity(
            routingKey,
            metadata.TraceParent,
            metadata.CorrelationId,
            metadata.MessageId);
        var traceParent = publishActivity?.Id ?? metadata.TraceParent;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = metadata.CorrelationId,
            ["messageId"] = metadata.MessageId,
            ["routingKey"] = routingKey
        });

        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"exchange:{_options.EventsExchange}?type=topic"));
        await endpoint.Send(payload, payloadType, context =>
        {
            if (Guid.TryParse(metadata.MessageId, out var messageId))
            {
                context.MessageId = messageId;
            }
            else
            {
                context.Headers.Set("message-id", metadata.MessageId);
            }

            if (Guid.TryParse(metadata.CorrelationId, out var correlationId))
            {
                context.CorrelationId = correlationId;
            }
            else
            {
                context.Headers.Set("correlation-id", metadata.CorrelationId);
            }

            if (!string.IsNullOrWhiteSpace(traceParent))
            {
                context.Headers.Set("traceparent", traceParent);
            }

            if (context is RabbitMqSendContext rabbitMq)
            {
                rabbitMq.RoutingKey = routingKey;
                rabbitMq.Durable = true;
            }
        }, ct);

        logger.LogInformation("Published event {RoutingKey}", routingKey);
    }

    private static object? DeserializeByRoutingKey(string routingKey, string payloadJson)
    {
        var type = routingKey switch
        {
            EventRoutingKeys.OrderCreated => typeof(OrderCreatedIntegrationEvent),
            EventRoutingKeys.StockReserved => typeof(StockReservedIntegrationEvent),
            EventRoutingKeys.StockRejected => typeof(StockRejectedIntegrationEvent),
            EventRoutingKeys.OrderConfirmed => typeof(OrderConfirmedIntegrationEvent),
            EventRoutingKeys.OrderRejected => typeof(OrderRejectedIntegrationEvent),
            _ => null
        };

        return type is null
            ? null
            : JsonSerializer.Deserialize(payloadJson, type, JsonDefaults.Options);
    }
}
