namespace ShopNGo.BuildingBlocks.Messaging;

public sealed record MessageContext(string MessageId, string CorrelationId, string RoutingKey, string? TraceParent, int RetryCount);
