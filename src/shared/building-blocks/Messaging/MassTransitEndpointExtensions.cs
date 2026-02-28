using MassTransit;
using MassTransit.RabbitMqTransport;

namespace ShopNGo.BuildingBlocks.Messaging;

public static class MassTransitEndpointExtensions
{
    public static RabbitMqThroughputSettings ResolveGlobalThroughput(this RabbitMqOptions options)
        => ResolveThroughputSettings(options, queueName: null);

    public static void ConfigureSubscriptionEndpoint<TConsumer>(
        this IRabbitMqBusFactoryConfigurator bus,
        IBusRegistrationContext context,
        RabbitMqOptions options,
        string queueName,
        string routingKey)
        where TConsumer : class, IConsumer
    {
        var throughput = ResolveThroughputSettings(options, queueName);

        bus.ReceiveEndpoint(queueName, endpoint =>
        {
            endpoint.ConfigureConsumeTopology = false;
            endpoint.PrefetchCount = throughput.PrefetchCount;
            if (throughput.ConcurrentMessageLimit is not null)
            {
                endpoint.ConcurrentMessageLimit = throughput.ConcurrentMessageLimit.Value;
            }

            endpoint.Bind(options.EventsExchange, binding =>
            {
                binding.ExchangeType = "topic";
                binding.RoutingKey = routingKey;
                binding.Durable = true;
            });

            if (options.MaxRetries > 0)
            {
                var interval = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryDelayMs));
                var intervals = Enumerable.Repeat(interval, options.MaxRetries).ToArray();
                endpoint.UseMessageRetry(retry => retry.Intervals(intervals));
            }

            endpoint.ConfigureConsumer<TConsumer>(context);
        });
    }

    private static RabbitMqThroughputSettings ResolveThroughputSettings(RabbitMqOptions options, string? queueName)
    {
        var defaults = ResolveLevelDefaults(options.Throughput.Level);
        var prefetch = options.Throughput.PrefetchOverride ?? defaults.PrefetchCount ?? options.PrefetchCount;
        int? concurrent = options.Throughput.ConcurrentMessageLimit ?? defaults.ConcurrentMessageLimit;

        if (!string.IsNullOrWhiteSpace(queueName)
            && options.Throughput.Endpoints.TryGetValue(queueName, out var endpoint))
        {
            var endpointDefaults = ResolveLevelDefaults(endpoint.Level);
            prefetch = endpoint.PrefetchOverride ?? endpointDefaults.PrefetchCount ?? prefetch;
            concurrent = endpoint.ConcurrentMessageLimit ?? endpointDefaults.ConcurrentMessageLimit ?? concurrent;
        }

        if (prefetch <= 0)
        {
            prefetch = 8;
        }

        if (concurrent <= 0)
        {
            concurrent = null;
        }

        var boundedPrefetch = (ushort)Math.Min(prefetch, ushort.MaxValue);
        return new RabbitMqThroughputSettings(boundedPrefetch, concurrent);
    }

    private static RabbitMqThroughputLevelDefaults ResolveLevelDefaults(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return default;
        }

        return level.Trim().ToLowerInvariant() switch
        {
            "conservative" => new RabbitMqThroughputLevelDefaults(PrefetchCount: 4, ConcurrentMessageLimit: 2),
            "balanced" => new RabbitMqThroughputLevelDefaults(PrefetchCount: 16, ConcurrentMessageLimit: 8),
            "aggressive" => new RabbitMqThroughputLevelDefaults(PrefetchCount: 64, ConcurrentMessageLimit: 24),
            _ => default
        };
    }

    public readonly record struct RabbitMqThroughputSettings(ushort PrefetchCount, int? ConcurrentMessageLimit);
    private readonly record struct RabbitMqThroughputLevelDefaults(int? PrefetchCount, int? ConcurrentMessageLimit);
}
