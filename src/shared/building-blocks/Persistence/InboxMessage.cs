using System.ComponentModel.DataAnnotations;

namespace ShopNGo.BuildingBlocks.Persistence;

public sealed class InboxMessage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Consumer { get; set; }

    [MaxLength(200)]
    public required string MessageId { get; set; }

    [MaxLength(200)]
    public required string RoutingKey { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}
