using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Messaging;
using System.Diagnostics;

namespace ShopNGo.BuildingBlocks.Persistence;

public abstract class OutboxDispatcherBackgroundService<TDbContext> : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly string _serviceName = typeof(TDbContext).Assembly.GetName().Name ?? typeof(TDbContext).Name;
    private readonly TimeSpan _pollInterval;
    private readonly int _batchSize;

    protected OutboxDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxDispatcherOptions> options,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var configuredPollIntervalMs = options.Value.PollIntervalMs;
        var configuredBatchSize = options.Value.BatchSize;

        if (configuredPollIntervalMs <= 0)
        {
            _logger.LogWarning(
                "Invalid OutboxDispatcher poll interval {PollIntervalMs}; falling back to 2000 ms",
                configuredPollIntervalMs);
            configuredPollIntervalMs = 2000;
        }

        if (configuredBatchSize <= 0)
        {
            _logger.LogWarning(
                "Invalid OutboxDispatcher batch size {BatchSize}; falling back to 50",
                configuredBatchSize);
            configuredBatchSize = 50;
        }

        _pollInterval = TimeSpan.FromMilliseconds(configuredPollIntervalMs);
        _batchSize = configuredBatchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatch loop failed for {Dispatcher}", GetType().Name);
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Multi-instance safety: each dispatcher locks a different outbox slice.
        var batch = await db.OutboxMessages
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM outbox_messages
                WHERE "DispatchedAtUtc" IS NULL
                ORDER BY "CreatedAtUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT {_batchSize}
                """)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["messageId"] = message.MessageId,
                ["correlationId"] = message.CorrelationId,
                ["routingKey"] = message.RoutingKey,
                ["outboxId"] = message.Id
            });

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var metadata = new MessageMetadata(message.MessageId, message.CorrelationId, message.TraceParent);
                await publisher.PublishRawAsync(message.RoutingKey, message.PayloadJson, metadata, ct);
                message.DispatchedAtUtc = DateTimeOffset.UtcNow;
                message.LastError = null;
                MessagingMetrics.RecordOutboxDispatch(_serviceName, message.RoutingKey, "success", stopwatch.Elapsed.TotalMilliseconds);
                _logger.LogInformation("Dispatched outbox message");
            }
            catch (Exception ex)
            {
                MessagingMetrics.RecordOutboxDispatch(_serviceName, message.RoutingKey, "failure", stopwatch.Elapsed.TotalMilliseconds);
                message.LastError = ex.Message;
                _logger.LogError(ex, "Failed dispatching outbox message {OutboxId} route {RoutingKey}", message.Id, message.RoutingKey);
            }
        }

        if (batch.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
