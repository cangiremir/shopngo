using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ShopNGo.IntegrationTests.Infrastructure;

internal sealed class RabbitMqTestClient
{
    private readonly ConnectionFactory _factory;
    private readonly string _eventsExchange;

    public RabbitMqTestClient(
        string hostName,
        int port,
        string userName,
        string password,
        string virtualHost,
        string eventsExchange)
    {
        _factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            VirtualHost = virtualHost
        };
        _eventsExchange = eventsExchange;
    }

    public Task PublishRawAsync(string routingKey, byte[] body, string messageId, string? correlationId = null)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(_eventsExchange, ExchangeType.Topic, durable: true, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = messageId;
        properties.CorrelationId = correlationId ?? messageId;

        channel.BasicPublish(_eventsExchange, routingKey, properties, body);
        return Task.CompletedTask;
    }

    public Task<uint> GetQueueMessageCountAsync(string queueName)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();
        try
        {
            var count = channel.QueueDeclarePassive(queueName).MessageCount;
            return Task.FromResult(count);
        }
        catch (OperationInterruptedException)
        {
            return Task.FromResult(0u);
        }
    }

    public async Task<RabbitCapturedMessage> CaptureNextPublishedEventAsync(string routingKey, Func<Task> trigger, int timeoutSeconds = 15)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(_eventsExchange, ExchangeType.Topic, durable: true, autoDelete: false);

        var tapQueue = channel.QueueDeclare(queue: string.Empty, durable: false, exclusive: true, autoDelete: true).QueueName;
        channel.QueueBind(tapQueue, _eventsExchange, routingKey);

        await trigger();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = channel.BasicGet(tapQueue, autoAck: true);
            if (result is not null)
            {
                return new RabbitCapturedMessage(
                    result.RoutingKey,
                    result.BasicProperties.MessageId,
                    result.BasicProperties.CorrelationId,
                    ParseHeaders(result.BasicProperties.Headers),
                    Encoding.UTF8.GetString(result.Body.ToArray()));
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"No message captured for routing key '{routingKey}'.");
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(IDictionary<string, object?>? rawHeaders)
    {
        if (rawHeaders is null || rawHeaders.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rawHeaders)
        {
            headers[pair.Key] = pair.Value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => pair.Value?.ToString() ?? string.Empty
            };
        }

        return headers;
    }
}
