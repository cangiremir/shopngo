using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using System.Text.RegularExpressions;

namespace ShopNGo.OrderService.Domain;

public enum OrderStatus
{
    PendingStock = 1,
    Confirmed = 2,
    Rejected = 3
}

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string NotificationChannel { get; private set; } = NotificationChannels.Email;
    public OrderStatus Status { get; private set; } = OrderStatus.PendingStock;
    public string? RejectionReasonCode { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public List<OrderItem> Items { get; set; } = [];

    public static Order Create(
        string customerEmail,
        string? customerPhone,
        string? notificationChannel,
        IEnumerable<(Guid productId, int quantity)> items)
    {
        var list = items.ToList();
        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Customer email is required.");
        }

        if (list.Count == 0)
        {
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Order must contain at least one item.");
        }

        if (list.Any(i => i.productId == Guid.Empty || i.quantity <= 0))
        {
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "All order items must have a valid product and positive quantity.");
        }

        var normalizedChannel = NormalizeNotificationChannel(notificationChannel);
        var normalizedPhone = NormalizeCustomerPhone(customerPhone);
        if (normalizedChannel == NotificationChannels.Sms && string.IsNullOrWhiteSpace(normalizedPhone))
        {
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Customer phone is required when notification channel is sms.");
        }

        return new Order
        {
            CustomerEmail = customerEmail.Trim(),
            CustomerPhone = normalizedPhone,
            NotificationChannel = normalizedChannel,
            Items = list.Select(i => new OrderItem { ProductId = i.productId, Quantity = i.quantity }).ToList()
        };
    }

    public string GetNotificationTarget()
        => NotificationChannel switch
        {
            NotificationChannels.Email => CustomerEmail,
            NotificationChannels.Sms when !string.IsNullOrWhiteSpace(CustomerPhone) => CustomerPhone!,
            NotificationChannels.Sms => throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Customer phone is required for sms notifications."),
            _ => throw new BusinessRuleException(ErrorCodes.NotificationInvalidChannel, $"Unsupported notification channel '{NotificationChannel}'.")
        };

    public void MarkConfirmed()
    {
        EnsurePending();
        Status = OrderStatus.Confirmed;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkRejected(string reasonCode, string reason)
    {
        EnsurePending();
        Status = OrderStatus.Rejected;
        RejectionReasonCode = reasonCode;
        RejectionReason = reason;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private void EnsurePending()
    {
        if (Status != OrderStatus.PendingStock)
        {
            throw new BusinessRuleException(ErrorCodes.OrderInvalidState, $"Order {Id} is {Status} and cannot transition.");
        }
    }

    private static string NormalizeNotificationChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel)
            ? NotificationChannels.Email
            : channel.Trim().ToLowerInvariant();

        return normalized switch
        {
            NotificationChannels.Email or NotificationChannels.Sms => normalized,
            _ => throw new BusinessRuleException(ErrorCodes.NotificationInvalidChannel, $"Unsupported notification channel '{channel}'.")
        };
    }

    private static string? NormalizeCustomerPhone(string? customerPhone)
    {
        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return null;
        }

        var normalized = customerPhone.Trim();
        if (!Regex.IsMatch(normalized, @"^\+?[1-9]\d{7,14}$"))
        {
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Customer phone format is invalid.");
        }

        return normalized;
    }
}

public sealed class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
