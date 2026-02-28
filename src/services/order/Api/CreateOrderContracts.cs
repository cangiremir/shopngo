using System.ComponentModel.DataAnnotations;
using FluentValidation;
using ShopNGo.OrderService.Domain;

namespace ShopNGo.OrderService.Api;

public sealed class CreateOrderRequest
{
    [Required, EmailAddress]
    public string CustomerEmail { get; set; } = string.Empty;

    [Phone]
    public string? CustomerPhone { get; set; }

    [Required]
    public string NotificationChannel { get; set; } = ShopNGo.Contracts.NotificationChannels.Email;

    [Required]
    public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public sealed class CreateOrderItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.NotificationChannel)
            .NotEmpty()
            .Must(value =>
            {
                var normalized = value?.Trim().ToLowerInvariant();
                return normalized is ShopNGo.Contracts.NotificationChannels.Email or ShopNGo.Contracts.NotificationChannels.Sms;
            })
            .WithMessage("Notification channel must be 'email' or 'sms'.");
        RuleFor(x => x.CustomerPhone)
            .Must(phone => string.IsNullOrWhiteSpace(phone) || IsE164Like(phone))
            .WithMessage("Customer phone must be a valid phone number in E.164-like format (e.g. +15551234567).");
        RuleFor(x => x.CustomerPhone)
            .NotEmpty()
            .When(x => string.Equals(x.NotificationChannel, ShopNGo.Contracts.NotificationChannels.Sms, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Customer phone is required when notificationChannel is 'sms'.");
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductId).NotEmpty();
            item.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }

    private static bool IsE164Like(string phone)
        => System.Text.RegularExpressions.Regex.IsMatch(phone.Trim(), @"^\+?[1-9]\d{7,14}$");
}

public sealed record OrderDto(
    Guid Id,
    string CustomerEmail,
    string? CustomerPhone,
    string NotificationChannel,
    string Status,
    string? RejectionReasonCode,
    string? RejectionReason,
    IReadOnlyCollection<OrderItemDto> Items,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.CustomerEmail,
        order.CustomerPhone,
        order.NotificationChannel,
        order.Status.ToString(),
        order.RejectionReasonCode,
        order.RejectionReason,
        order.Items.Select(i => new OrderItemDto(i.ProductId, i.Quantity)).ToArray(),
        order.CreatedAtUtc,
        order.UpdatedAtUtc);
}

public sealed record OrderItemDto(Guid ProductId, int Quantity);
