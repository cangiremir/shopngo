using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.BuildingBlocks.Metrics;
using ShopNGo.Contracts;
using ShopNGo.OrderService.Api;
using ShopNGo.OrderService.Data;
using ShopNGo.OrderService.Domain;

namespace ShopNGo.OrderService.Application;

public sealed class OrderApplicationService(OrderDbContext db, ILogger<OrderApplicationService> logger)
{
    private const string ServiceName = "ShopNGo.OrderService";

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request, string correlationId, string? traceParent, CancellationToken ct)
    {
        var order = Order.Create(
            request.CustomerEmail,
            request.CustomerPhone,
            request.NotificationChannel,
            request.Items.Select(i => (i.ProductId, i.Quantity)));

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["orderId"] = order.Id
        });

        db.Orders.Add(order);
        db.AddOutbox(
            EventRoutingKeys.OrderCreated,
            new OrderCreatedIntegrationEvent(
                order.Id,
                order.CustomerEmail,
                order.Items.Select(i => new OrderItemContract(i.ProductId, i.Quantity)).ToArray(),
                DateTimeOffset.UtcNow,
                order.NotificationChannel,
                order.GetNotificationTarget()),
            correlationId,
            traceParent);

        await db.SaveChangesAsync(ct);
        BusinessMetrics.RecordOrderCreated(ServiceName);
        logger.LogInformation("Created order {OrderId} in PendingStock", order.Id);
        return order;
    }

    public async Task ConfirmOrderAsync(Guid orderId, string correlationId, string? traceParent, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["orderId"] = orderId
        });

        var order = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new BusinessRuleException(ErrorCodes.OrderNotFound, $"Order {orderId} not found.");

        order.MarkConfirmed();
        db.AddOutbox(
            EventRoutingKeys.OrderConfirmed,
            new OrderConfirmedIntegrationEvent(
                order.Id,
                order.CustomerEmail,
                DateTimeOffset.UtcNow,
                order.NotificationChannel,
                order.GetNotificationTarget()),
            correlationId,
            traceParent);

        await db.SaveChangesAsync(ct);
        BusinessMetrics.RecordOrderFinalized(ServiceName, "confirmed", errorCode: null);
        logger.LogInformation("Confirmed order {OrderId}", orderId);
    }

    public async Task RejectOrderAsync(Guid orderId, string reasonCode, string reason, string correlationId, string? traceParent, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["orderId"] = orderId,
            ["errorCode"] = reasonCode
        });

        var order = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new BusinessRuleException(ErrorCodes.OrderNotFound, $"Order {orderId} not found.");

        order.MarkRejected(reasonCode, reason);
        db.AddOutbox(
            EventRoutingKeys.OrderRejected,
            new OrderRejectedIntegrationEvent(
                order.Id,
                order.CustomerEmail,
                reasonCode,
                reason,
                DateTimeOffset.UtcNow,
                order.NotificationChannel,
                order.GetNotificationTarget()),
            correlationId,
            traceParent);

        await db.SaveChangesAsync(ct);
        BusinessMetrics.RecordOrderFinalized(ServiceName, "rejected", reasonCode);
        logger.LogInformation("Rejected order {OrderId} with reason {ReasonCode}", orderId, reasonCode);
    }
}
