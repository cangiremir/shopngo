using System.ComponentModel.DataAnnotations;

namespace ShopNGo.BuildingBlocks.Persistence;

public sealed class OutboxMessage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string MessageId { get; set; }

    [MaxLength(200)]
    public required string RoutingKey { get; set; }

    [MaxLength(200)]
    public required string CorrelationId { get; set; }

    [MaxLength(200)]
    public string? TraceParent { get; set; }

    public required string PayloadJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }
}
