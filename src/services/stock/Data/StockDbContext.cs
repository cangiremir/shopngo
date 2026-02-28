using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.BuildingBlocks.Serialization;
using ShopNGo.StockService.Domain;

namespace ShopNGo.StockService.Data;

public sealed class StockDbContext(DbContextOptions<StockDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(b =>
        {
            b.ToTable("inventory_items");
            b.HasKey(x => x.ProductId);
            b.Property(x => x.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(0);
        });

        modelBuilder.Entity<StockReservation>(b =>
        {
            b.ToTable("stock_reservations");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OrderId).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasIndex(x => x.MessageId).IsUnique();
            b.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasIndex(x => new { x.Consumer, x.MessageId }).IsUnique();
        });
    }

    public void AddOutbox<T>(string routingKey, T payload, string correlationId, string? traceParent)
    {
        OutboxMessages.Add(new OutboxMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            RoutingKey = routingKey,
            CorrelationId = correlationId,
            TraceParent = traceParent,
            PayloadJson = JsonSerializer.Serialize(payload, JsonDefaults.Options)
        });
    }
}
