namespace ShopNGo.StockService.Domain;

public sealed class InventoryItem
{
    public Guid ProductId { get; set; }
    public int AvailableQuantity { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StockReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public DateTimeOffset ReservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
