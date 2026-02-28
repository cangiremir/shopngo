using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using ShopNGo.StockService.Data;
using ShopNGo.StockService.Domain;

namespace ShopNGo.StockService.Application;

public sealed record ReservationDemandLine(Guid ProductId, int RequiredQuantity);
public sealed record StockReservationStoreResult(string Result, string? ErrorCode, string? ErrorReason);

public interface IStockReservationStore
{
    Task<StockReservationStoreResult> ReserveAsync(
        Guid orderId,
        IReadOnlyCollection<ReservationDemandLine> demandLines,
        bool usePessimisticLocking,
        string correlationId,
        string? traceParent,
        CancellationToken ct);
}

public sealed class StockReservationStore(
    StockDbContext db,
    ILogger<StockReservationStore> logger) : IStockReservationStore
{
    public async Task<StockReservationStoreResult> ReserveAsync(
        Guid orderId,
        IReadOnlyCollection<ReservationDemandLine> demandLines,
        bool usePessimisticLocking,
        string correlationId,
        string? traceParent,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.StockReservations.AnyAsync(x => x.OrderId == orderId, ct))
        {
            logger.LogInformation("Order {OrderId} already reserved; skipping", orderId);
            await tx.CommitAsync(ct);
            return new StockReservationStoreResult("duplicate_skip", ErrorCode: null, ErrorReason: null);
        }

        if (usePessimisticLocking)
        {
            await LockInventoryRowsAsync(demandLines, ct);
        }

        var rejection = await ConsumeInventoryAsync(demandLines, ct);
        if (rejection is not null)
        {
            await tx.RollbackAsync(ct);
            return new StockReservationStoreResult("rejected", rejection.ErrorCode, rejection.ErrorReason);
        }

        await StoreReservationAndOutboxAsync(orderId, correlationId, traceParent, ct);
        await tx.CommitAsync(ct);
        logger.LogInformation(
            "Reserved stock for order {OrderId} using {LockMode} mode",
            orderId,
            usePessimisticLocking ? "pessimistic" : "optimistic");

        return new StockReservationStoreResult("reserved", ErrorCode: null, ErrorReason: null);
    }

    private async Task LockInventoryRowsAsync(IReadOnlyCollection<ReservationDemandLine> demandLines, CancellationToken ct)
    {
        foreach (var line in demandLines)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM inventory_items WHERE \"ProductId\" = {line.ProductId} FOR UPDATE",
                ct);
        }
    }

    private async Task<ReservationRejection?> ConsumeInventoryAsync(IReadOnlyCollection<ReservationDemandLine> demandLines, CancellationToken ct)
    {
        foreach (var line in demandLines)
        {
            var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE inventory_items
                SET "AvailableQuantity" = "AvailableQuantity" - {line.RequiredQuantity},
                    "Version" = "Version" + 1,
                    "UpdatedAtUtc" = {DateTimeOffset.UtcNow}
                WHERE "ProductId" = {line.ProductId}
                  AND "AvailableQuantity" >= {line.RequiredQuantity}
                """,
                ct);

            if (rowsAffected == 0)
            {
                var (errorCode, errorReason) = await ResolveReservationFailureAsync(line.ProductId, ct);
                return new ReservationRejection(errorCode, errorReason);
            }
        }

        return null;
    }

    private Task StoreReservationAndOutboxAsync(Guid orderId, string correlationId, string? traceParent, CancellationToken ct)
    {
        db.StockReservations.Add(new StockReservation { OrderId = orderId });
        db.AddOutbox(
            EventRoutingKeys.StockReserved,
            new StockReservedIntegrationEvent(orderId, DateTimeOffset.UtcNow),
            correlationId,
            traceParent);
        return db.SaveChangesAsync(ct);
    }

    private async Task<(string ErrorCode, string ErrorReason)> ResolveReservationFailureAsync(Guid productId, CancellationToken ct)
    {
        var exists = await db.InventoryItems
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == productId, ct);

        return exists
            ? (ErrorCodes.InsufficientStock, $"Insufficient stock for product {productId}.")
            : (ErrorCodes.ProductNotFound, $"Product {productId} not found.");
    }

    private sealed record ReservationRejection(string ErrorCode, string ErrorReason);
}
