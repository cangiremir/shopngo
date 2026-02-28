using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.BuildingBlocks.Metrics;
using ShopNGo.Contracts;
using ShopNGo.StockService.Api;
using ShopNGo.StockService.Data;
using ShopNGo.StockService.Domain;
using System.Buffers.Binary;
using System.Diagnostics;

namespace ShopNGo.StockService.Application;

public sealed class StockApplicationService(
    StockDbContext db,
    IOptions<StockConcurrencyOptions> concurrencyOptions,
    IStockReservationStore reservationStore,
    IStockGuardrail guardrail,
    ILogger<StockApplicationService> logger)
{
    private const string ServiceName = "ShopNGo.StockService";
    private const string TechnicalFailureErrorCode = "TECHNICAL_FAILURE";
    private readonly StockConcurrencyOptions _concurrency = concurrencyOptions.Value;

    public async Task SeedStockAsync(SeedStockRequest request, string correlationId, string? traceParent, CancellationToken ct)
    {
        if (request.Items.Count == 0)
        {
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Seed request must contain items.");
        }

        foreach (var incoming in request.Items)
        {
            var entity = await db.InventoryItems.FirstOrDefaultAsync(x => x.ProductId == incoming.ProductId, ct);
            if (entity is null)
            {
                db.InventoryItems.Add(new InventoryItem
                {
                    ProductId = incoming.ProductId,
                    AvailableQuantity = incoming.Quantity,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                entity.AvailableQuantity += incoming.Quantity;
                entity.Version += 1;
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded stock for {Count} item(s); correlationId={CorrelationId}", request.Items.Count, correlationId);
    }

    public async Task HandleOrderCreatedAsync(OrderCreatedIntegrationEvent evt, string correlationId, string? traceParent, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var recorded = false;
        var guardrailLeases = new List<StockGuardrailLease>();

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["orderId"] = evt.OrderId
        });

        try
        {
            var guardrailAcquisition = await AcquireGuardrailLeasesAsync(evt, ct);
            guardrailLeases.AddRange(guardrailAcquisition.Leases);
            if (!guardrailAcquisition.Allowed)
            {
                await HandleGuardrailDenyAsync(evt, guardrailAcquisition, correlationId, traceParent, ct);
                RecordStockReservationMetric(ref recorded, stopwatch, "rejected", guardrailAcquisition.ErrorCode);
                return;
            }

            var mode = await ResolveConcurrencyModeAsync(evt, guardrailLeases, ct);
            logger.LogInformation("Handling order {OrderId} with stock concurrency mode {Mode}", evt.OrderId, mode);

            var usePessimisticLocking = mode == StockConcurrencyMode.Pessimistic;
            var outcome = await ReserveStockForOrderAsync(evt, correlationId, traceParent, usePessimisticLocking, ct);

            RecordStockReservationMetric(ref recorded, stopwatch, outcome.Result, outcome.ErrorCode);
        }
        catch
        {
            RecordStockReservationMetric(ref recorded, stopwatch, "technical_failure", TechnicalFailureErrorCode);
            throw;
        }
        finally
        {
            await ReleaseGuardrailLeasesAsync(guardrailLeases);
        }
    }

    private async Task<GuardrailAcquisitionResult> AcquireGuardrailLeasesAsync(OrderCreatedIntegrationEvent evt, CancellationToken ct)
    {
        var leases = new List<StockGuardrailLease>();
        foreach (var productId in DistinctOrderedProductIds(evt))
        {
            var lease = await guardrail.AcquireAsync(productId, ct);
            if (lease.Allowed)
            {
                leases.Add(lease);
                continue;
            }

            await lease.DisposeAsync();
            var (errorCode, errorReason) = MapGuardrailDenial(lease.Reason, productId);
            return new GuardrailAcquisitionResult(
                leases,
                Allowed: false,
                ProductId: productId,
                GuardrailReason: lease.Reason,
                ErrorCode: errorCode,
                ErrorReason: errorReason);
        }

        return new GuardrailAcquisitionResult(leases, Allowed: true, null, null, null, null);
    }

    private async Task HandleGuardrailDenyAsync(
        OrderCreatedIntegrationEvent evt,
        GuardrailAcquisitionResult guardrailAcquisition,
        string correlationId,
        string? traceParent,
        CancellationToken ct)
    {
        await EmitRejectionAsync(
            evt.OrderId,
            guardrailAcquisition.ErrorCode!,
            guardrailAcquisition.ErrorReason!,
            correlationId,
            traceParent,
            ct);

        logger.LogWarning(
            "Rejected order {OrderId} because stock guardrail denied product {ProductId}; reason={GuardrailReason}; mappedErrorCode={ErrorCode}",
            evt.OrderId,
            guardrailAcquisition.ProductId,
            guardrailAcquisition.GuardrailReason ?? "unknown",
            guardrailAcquisition.ErrorCode);
    }

    private static async Task ReleaseGuardrailLeasesAsync(IEnumerable<StockGuardrailLease> leases)
    {
        foreach (var lease in leases)
        {
            await lease.DisposeAsync();
        }
    }

    private async Task<ReservationAttemptResult> ReserveStockForOrderAsync(
        OrderCreatedIntegrationEvent evt,
        string correlationId,
        string? traceParent,
        bool usePessimisticLocking,
        CancellationToken ct)
    {
        var demandLines = BuildReservationDemandLines(evt);
        var storeResult = await reservationStore.ReserveAsync(
            evt.OrderId,
            demandLines,
            usePessimisticLocking,
            correlationId,
            traceParent,
            ct);

        if (storeResult.Result != "rejected")
        {
            return new ReservationAttemptResult(storeResult.Result, storeResult.ErrorCode);
        }

        if (string.IsNullOrWhiteSpace(storeResult.ErrorCode) || string.IsNullOrWhiteSpace(storeResult.ErrorReason))
        {
            throw new InvalidOperationException("Stock reservation rejection requires error details.");
        }

        await EmitRejectionAsync(
            evt.OrderId,
            storeResult.ErrorCode,
            storeResult.ErrorReason,
            correlationId,
            traceParent,
            ct);
        return new ReservationAttemptResult("rejected", storeResult.ErrorCode);
    }

    private static IReadOnlyCollection<ReservationDemandLine> BuildReservationDemandLines(OrderCreatedIntegrationEvent evt)
    {
        return evt.Items
            .GroupBy(x => x.ProductId)
            .Select(g => new ReservationDemandLine(g.Key, g.Sum(x => x.Quantity)))
            .OrderBy(x => x.ProductId)
            .ToArray();
    }

    private static (string ErrorCode, string ErrorReason) MapGuardrailDenial(string? reason, Guid productId)
    {
        return reason?.ToLowerInvariant() switch
        {
            "admission_limited" => (
                ErrorCodes.StockAdmissionLimited,
                $"Stock guardrail admission limit reached for product {productId}."),
            "redis_unavailable" or "redis_error" => (
                ErrorCodes.StockGuardrailUnavailable,
                $"Stock guardrail unavailable for product {productId}."),
            _ => (
                ErrorCodes.StockGuardrailBlocked,
                $"Stock guardrail blocked product {productId}; reason={reason ?? "unknown"}.")
        };
    }

    private async Task<StockConcurrencyMode> ResolveConcurrencyModeAsync(
        OrderCreatedIntegrationEvent evt,
        IReadOnlyCollection<StockGuardrailLease> guardrailLeases,
        CancellationToken ct)
    {
        var configuredMode = ParseConcurrencyMode(_concurrency.Mode);
        if (configuredMode != StockConcurrencyMode.Hybrid)
        {
            return configuredMode;
        }

        var hybrid = _concurrency.Hybrid;
        var canaryPercent = Math.Clamp(hybrid.CanaryPercent, 0, 100);
        if (!hybrid.Enabled || canaryPercent == 0)
        {
            return StockConcurrencyMode.Pessimistic;
        }

        if (hybrid.ConservativeOnGuardrailFailure && guardrailLeases.Any(x => !x.MeasurementAvailable))
        {
            return StockConcurrencyMode.Pessimistic;
        }

        if (guardrailLeases.Any(x => x.IsHotSku))
        {
            return StockConcurrencyMode.Pessimistic;
        }

        var productIds = evt.Items.Select(x => x.ProductId).Distinct().ToArray();
        if (productIds.Any(productId => !IsInCanary(productId, canaryPercent)))
        {
            return StockConcurrencyMode.Pessimistic;
        }

        if (hybrid.LowStockThreshold > 0)
        {
            var hasLowStock = await db.InventoryItems
                .Where(x => productIds.Contains(x.ProductId))
                .AnyAsync(x => x.AvailableQuantity <= hybrid.LowStockThreshold, ct);

            if (hasLowStock)
            {
                return StockConcurrencyMode.Pessimistic;
            }
        }

        return StockConcurrencyMode.Optimistic;
    }

    private static StockConcurrencyMode ParseConcurrencyMode(string? configuredMode)
        => Enum.TryParse<StockConcurrencyMode>(configuredMode, ignoreCase: true, out var mode)
            ? mode
            : StockConcurrencyMode.Pessimistic;

    private static bool IsInCanary(Guid productId, int canaryPercent)
    {
        if (canaryPercent <= 0)
        {
            return false;
        }

        if (canaryPercent >= 100)
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[16];
        productId.TryWriteBytes(bytes);
        var hash = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return (hash % 100) < canaryPercent;
    }

    private static IEnumerable<Guid> DistinctOrderedProductIds(OrderCreatedIntegrationEvent evt)
        => evt.Items.Select(x => x.ProductId).Distinct().OrderBy(x => x);

    private static void RecordStockReservationMetric(ref bool recorded, Stopwatch stopwatch, string result, string? errorCode = null)
    {
        if (recorded)
        {
            return;
        }

        BusinessMetrics.RecordStockReservationResult(
            ServiceName,
            result,
            errorCode,
            stopwatch.Elapsed.TotalMilliseconds);
        recorded = true;
    }

    private async Task EmitRejectionAsync(Guid orderId, string reasonCode, string reason, string correlationId, string? traceParent, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["orderId"] = orderId,
            ["errorCode"] = reasonCode
        });

        db.AddOutbox(
            EventRoutingKeys.StockRejected,
            new StockRejectedIntegrationEvent(orderId, reasonCode, reason, DateTimeOffset.UtcNow),
            correlationId,
            traceParent);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Rejected stock for order {OrderId}; reason {ReasonCode}", orderId, reasonCode);
    }

    private sealed record ReservationAttemptResult(string Result, string? ErrorCode);
    private sealed record GuardrailAcquisitionResult(
        IReadOnlyCollection<StockGuardrailLease> Leases,
        bool Allowed,
        Guid? ProductId,
        string? GuardrailReason,
        string? ErrorCode,
        string? ErrorReason);
}
