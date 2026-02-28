namespace ShopNGo.BuildingBlocks.Messaging;

public sealed record MessageMetadata(string MessageId, string CorrelationId, string? TraceParent = null)
{
    public static MessageMetadata Create(string? correlationId = null, string? traceParent = null)
        => new(Guid.NewGuid().ToString("N"), correlationId ?? Guid.NewGuid().ToString("N"), traceParent);
}
