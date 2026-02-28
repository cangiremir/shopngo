using System.Diagnostics.Metrics;

namespace ShopNGo.BuildingBlocks.Messaging;

public static class MessagingMetrics
{
    public const string MeterName = "ShopNGo.Messaging";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> ConsumerResultCounter =
        Meter.CreateCounter<long>("shopngo_consumer_messages_total", description: "Consumer processing outcomes.");

    private static readonly Histogram<double> ConsumerDurationMsHistogram =
        Meter.CreateHistogram<double>("shopngo_consumer_duration_ms", unit: "ms", description: "Consumer processing duration.");

    private static readonly Counter<long> ConsumerRepublishCounter =
        Meter.CreateCounter<long>("shopngo_consumer_republish_total", description: "Messages republished to retry or DLQ exchanges.");

    private static readonly Counter<long> OutboxDispatchCounter =
        Meter.CreateCounter<long>("shopngo_outbox_dispatch_total", description: "Outbox dispatch outcomes.");

    private static readonly Histogram<double> OutboxDispatchDurationMsHistogram =
        Meter.CreateHistogram<double>("shopngo_outbox_dispatch_duration_ms", unit: "ms", description: "Outbox dispatch duration.");

    public static void RecordConsumerResult(
        string service,
        string consumer,
        string routingKey,
        string result,
        string? errorCode,
        double durationMs)
    {
        var normalizedError = string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode;

        ConsumerResultCounter.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("routing_key", routingKey),
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("error_code", normalizedError));

        ConsumerDurationMsHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("routing_key", routingKey),
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("error_code", normalizedError));
    }

    public static void RecordRepublish(
        string service,
        string consumer,
        string routingKey,
        string target,
        string? errorCode)
    {
        ConsumerRepublishCounter.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("routing_key", routingKey),
            new KeyValuePair<string, object?>("target", target),
            new KeyValuePair<string, object?>("error_code", string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode));
    }

    public static void RecordOutboxDispatch(
        string service,
        string routingKey,
        string result,
        double durationMs)
    {
        OutboxDispatchCounter.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("routing_key", routingKey),
            new KeyValuePair<string, object?>("result", result));

        OutboxDispatchDurationMsHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("routing_key", routingKey),
            new KeyValuePair<string, object?>("result", result));
    }
}
