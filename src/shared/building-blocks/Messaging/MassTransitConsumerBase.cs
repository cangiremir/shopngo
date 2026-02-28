using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.BuildingBlocks.Persistence;

namespace ShopNGo.BuildingBlocks.Messaging;

public abstract class MassTransitConsumerBase<TMessage, TDbContext> : IConsumer<TMessage>
    where TMessage : class
    where TDbContext : DbContext, IInboxDbContext
{
    private readonly RabbitMqOptions _rabbit;
    private readonly ILogger _logger;
    private readonly string _serviceName = typeof(TDbContext).Assembly.GetName().Name ?? typeof(TDbContext).Name;

    protected MassTransitConsumerBase(
        TDbContext db,
        InboxProcessor<TDbContext> inbox,
        IOptions<RabbitMqOptions> rabbitOptions,
        ILogger logger)
    {
        Db = db;
        Inbox = inbox;
        _rabbit = rabbitOptions.Value;
        _logger = logger;
    }

    protected TDbContext Db { get; }
    protected InboxProcessor<TDbContext> Inbox { get; }

    protected abstract string ConsumerName { get; }
    protected abstract string RoutingKey { get; }

    protected abstract Task HandleAsync(
        MessageContext context,
        TMessage message,
        ConsumeContext<TMessage> consumeContext,
        CancellationToken ct);

    protected virtual Task OnBusinessExceptionAsync(
        MessageContext context,
        TMessage message,
        BusinessRuleException ex,
        ConsumeContext<TMessage> consumeContext,
        CancellationToken ct)
    {
        _logger.LogWarning(ex, "Business exception in {ConsumerName} errorCode={ErrorCode}", ConsumerName, ex.ErrorCode);
        return Task.CompletedTask;
    }

    public async Task Consume(ConsumeContext<TMessage> consumeContext)
    {
        var orderId = TryExtractOrderId(consumeContext.Message);
        var messageId = ResolveMessageId(consumeContext, orderId);
        var correlationId = ResolveCorrelationId(consumeContext, orderId, messageId);
        var traceParent = consumeContext.Headers.Get<string>("traceparent") ?? Activity.Current?.Id;
        var retryCount = consumeContext.GetRetryAttempt();
        var context = new MessageContext(messageId, correlationId, RoutingKey, traceParent, retryCount);
        var stopwatch = Stopwatch.StartNew();

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = context.CorrelationId,
            ["messageId"] = context.MessageId,
            ["routingKey"] = context.RoutingKey,
            ["retryCount"] = context.RetryCount,
            ["consumer"] = ConsumerName,
            ["orderId"] = orderId
        });

        using var consumeActivity = MessagingDiagnostics.StartConsumerActivity(context, ConsumerName, orderId);

        try
        {
            var shouldProcess = await Inbox.BeginAsync(Db, ConsumerName, context.MessageId, context.RoutingKey, consumeContext.CancellationToken);
            if (!shouldProcess)
            {
                MessagingMetrics.RecordConsumerResult(
                    _serviceName,
                    ConsumerName,
                    context.RoutingKey,
                    "duplicate_skip",
                    errorCode: null,
                    stopwatch.Elapsed.TotalMilliseconds);
                return;
            }

            await HandleAsync(context, consumeContext.Message, consumeContext, consumeContext.CancellationToken);
            await Inbox.CompleteAsync(Db, ConsumerName, context.MessageId, consumeContext.CancellationToken);
            MessagingMetrics.RecordConsumerResult(
                _serviceName,
                ConsumerName,
                context.RoutingKey,
                "success",
                errorCode: null,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (BusinessRuleException ex)
        {
            using var businessScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["errorCode"] = ex.ErrorCode
            });

            await OnBusinessExceptionAsync(context, consumeContext.Message, ex, consumeContext, consumeContext.CancellationToken);
            await Inbox.CompleteAsync(Db, ConsumerName, context.MessageId, consumeContext.CancellationToken);
            MessagingMetrics.RecordConsumerResult(
                _serviceName,
                ConsumerName,
                context.RoutingKey,
                "business_exception",
                ex.ErrorCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            using var technicalScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["errorCode"] = "TECHNICAL_FAILURE"
            });

            _logger.LogError(ex, "Technical failure in {ConsumerName}; routingKey={RoutingKey}", ConsumerName, context.RoutingKey);
            var target = context.RetryCount >= _rabbit.MaxRetries ? "dlq" : "retry";
            MessagingMetrics.RecordRepublish(_serviceName, ConsumerName, context.RoutingKey, target, "TECHNICAL_FAILURE");
            MessagingMetrics.RecordConsumerResult(
                _serviceName,
                ConsumerName,
                context.RoutingKey,
                "technical_exception",
                "TECHNICAL_FAILURE",
                stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
    }

    private static string? TryExtractOrderId(TMessage message)
    {
        var property = message.GetType().GetProperty("OrderId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property?.PropertyType != typeof(Guid))
        {
            return null;
        }

        var value = property.GetValue(message);
        return value is Guid id ? id.ToString() : null;
    }

    private string ResolveMessageId(ConsumeContext<TMessage> consumeContext, string? orderId)
    {
        var candidates = new[]
        {
            consumeContext.MessageId?.ToString("N"),
            consumeContext.Headers.Get<string>("message-id"),
            consumeContext.Headers.Get<string>("MessageId"),
            consumeContext.Headers.Get<string>("MT-MessageId"),
            consumeContext.CorrelationId?.ToString("N"),
            consumeContext.Headers.Get<string>("correlation-id"),
            consumeContext.Headers.Get<string>("CorrelationId")
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeIdCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            return $"order:{orderId}:{RoutingKey}";
        }

        var payloadJson = JsonSerializer.Serialize(consumeContext.Message);
        var bytes = Encoding.UTF8.GetBytes($"{RoutingKey}:{payloadJson}");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return $"sha256:{hash}";
    }

    private string ResolveCorrelationId(ConsumeContext<TMessage> consumeContext, string? orderId, string messageId)
    {
        var candidates = new[]
        {
            consumeContext.CorrelationId?.ToString("N"),
            consumeContext.Headers.Get<string>("correlation-id"),
            consumeContext.Headers.Get<string>("CorrelationId"),
            consumeContext.Headers.Get<string>("message-id"),
            consumeContext.Headers.Get<string>("MessageId")
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeIdCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return !string.IsNullOrWhiteSpace(orderId) ? orderId : messageId;
    }

    private static string? NormalizeIdCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();
        return Guid.TryParse(trimmed, out var parsed)
            ? parsed.ToString("N")
            : trimmed;
    }
}
