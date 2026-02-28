using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using System.Text.RegularExpressions;

namespace ShopNGo.NotificationService.Domain;

public sealed class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Channel { get; set; } = "email";
    public string Template { get; set; } = string.Empty;
    public string Status { get; set; } = "Sent";
    public string? ErrorCode { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static NotificationLog Create(Guid orderId, string target, string template, string payload)
        => Create(orderId, target, NotificationChannels.Email, template, payload);

    public static NotificationLog Create(Guid orderId, string target, string channel, string template, string payload)
    {
        var normalizedChannel = NormalizeChannel(channel);
        var normalizedTarget = NormalizeTarget(normalizedChannel, target);

        return new NotificationLog
        {
            OrderId = orderId,
            Target = normalizedTarget,
            Channel = normalizedChannel,
            Template = template,
            Payload = payload
        };
    }

    public static NotificationLog Failed(Guid orderId, string target, string template, string payload, string errorCode)
        => Failed(orderId, target, NotificationChannels.Email, template, payload, errorCode);

    public static NotificationLog Failed(Guid orderId, string target, string channel, string template, string payload, string errorCode)
        => new()
        {
            OrderId = orderId,
            Target = target,
            Channel = string.IsNullOrWhiteSpace(channel) ? NotificationChannels.Email : channel.Trim().ToLowerInvariant(),
            Template = template,
            Payload = payload,
            Status = "Rejected",
            ErrorCode = errorCode
        };

    private static string NormalizeChannel(string channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel)
            ? NotificationChannels.Email
            : channel.Trim().ToLowerInvariant();

        return normalized switch
        {
            NotificationChannels.Email or NotificationChannels.Sms => normalized,
            _ => throw new BusinessRuleException(ErrorCodes.NotificationInvalidChannel, $"Notification channel '{channel}' is invalid.")
        };
    }

    private static string NormalizeTarget(string channel, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new BusinessRuleException(ErrorCodes.NotificationInvalidTarget, "Notification target is required.");
        }

        var normalizedTarget = target.Trim();

        if (channel == NotificationChannels.Email)
        {
            if (!normalizedTarget.Contains('@'))
            {
                throw new BusinessRuleException(ErrorCodes.NotificationInvalidTarget, "Notification email target is invalid.");
            }

            return normalizedTarget;
        }

        if (!Regex.IsMatch(normalizedTarget, @"^\+?[1-9]\d{7,14}$"))
        {
            throw new BusinessRuleException(ErrorCodes.NotificationInvalidTarget, "Notification sms target is invalid.");
        }

        return normalizedTarget;
    }
}
