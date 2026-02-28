using System.Diagnostics;

namespace ShopNGo.BuildingBlocks.Messaging;

public static class MessagingDiagnostics
{
    public const string ActivitySourceName = "ShopNGo.Messaging";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static Activity? StartProducerActivity(string routingKey, string? parentTraceParent, string correlationId, string messageId)
    {
        var activity = StartActivity($"{routingKey} publish", ActivityKind.Producer, parentTraceParent);
        Enrich(activity, routingKey, correlationId, messageId, operation: "publish");
        return activity;
    }

    public static Activity? StartConsumerActivity(MessageContext context, string consumerName, string? orderId)
    {
        var activity = StartActivity($"{context.RoutingKey} process", ActivityKind.Consumer, context.TraceParent);
        Enrich(activity, context.RoutingKey, context.CorrelationId, context.MessageId, operation: "process");
        if (activity is not null)
        {
            activity.SetTag("messaging.consumer.name", consumerName);
            activity.SetTag("messaging.message.retry.count", context.RetryCount);
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                activity.SetTag("order.id", orderId);
            }
        }

        return activity;
    }

    private static Activity? StartActivity(string name, ActivityKind kind, string? parentTraceParent)
    {
        if (!string.IsNullOrWhiteSpace(parentTraceParent)
            && ActivityContext.TryParse(parentTraceParent, traceState: null, out var parentContext))
        {
            return ActivitySource.StartActivity(name, kind, parentContext);
        }

        return ActivitySource.StartActivity(name, kind);
    }

    private static void Enrich(Activity? activity, string routingKey, string correlationId, string messageId, string operation)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.destination.name", "ecommerce.events");
        activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
        activity.SetTag("messaging.operation", operation);
        activity.SetTag("messaging.message.id", messageId);
        activity.SetTag("messaging.conversation_id", correlationId);
    }
}
