using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using ShopNGo.NotificationService.Domain;

namespace ShopNGo.NotificationService.Application;

public sealed record NotificationDispatchRequest(
    Guid OrderId,
    string Channel,
    string Target,
    string Template,
    string PayloadJson);

public interface INotificationChannelHandler
{
    string Channel { get; }
    NotificationLog SimulateSend(NotificationDispatchRequest request);
}

public sealed class EmailNotificationChannelHandler(ILogger<EmailNotificationChannelHandler> logger) : INotificationChannelHandler
{
    public string Channel => NotificationChannels.Email;

    public NotificationLog SimulateSend(NotificationDispatchRequest request)
    {
        var log = NotificationLog.Create(request.OrderId, request.Target, Channel, request.Template, request.PayloadJson);
        logger.LogInformation("Simulated EMAIL notification dispatch for order {OrderId} using template {Template}", request.OrderId, request.Template);
        return log;
    }
}

public sealed class SmsNotificationChannelHandler(ILogger<SmsNotificationChannelHandler> logger) : INotificationChannelHandler
{
    public string Channel => NotificationChannels.Sms;

    public NotificationLog SimulateSend(NotificationDispatchRequest request)
    {
        var log = NotificationLog.Create(request.OrderId, request.Target, Channel, request.Template, request.PayloadJson);
        logger.LogInformation("Simulated SMS notification dispatch for order {OrderId} using template {Template}", request.OrderId, request.Template);
        return log;
    }
}

public sealed class NotificationDispatchService(IEnumerable<INotificationChannelHandler> handlers)
{
    private readonly Dictionary<string, INotificationChannelHandler> _handlers =
        handlers.ToDictionary(x => x.Channel, StringComparer.OrdinalIgnoreCase);

    public NotificationLog Dispatch(NotificationDispatchRequest request)
    {
        if (!_handlers.TryGetValue(request.Channel, out var handler))
        {
            throw new BusinessRuleException(
                ErrorCodes.NotificationInvalidChannel,
                $"Notification channel '{request.Channel}' is not supported.");
        }

        return handler.SimulateSend(request);
    }
}
