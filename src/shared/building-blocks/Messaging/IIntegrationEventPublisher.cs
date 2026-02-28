namespace ShopNGo.BuildingBlocks.Messaging;

public interface IIntegrationEventPublisher
{
    Task PublishAsync<T>(string routingKey, T message, MessageMetadata metadata, CancellationToken ct = default);
    Task PublishRawAsync(string routingKey, string payloadJson, MessageMetadata metadata, CancellationToken ct = default);
}
