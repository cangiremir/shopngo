using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.BuildingBlocks.Serialization;
using ShopNGo.Contracts;
using ShopNGo.OrderService.Domain;

namespace ShopNGo.OrderService.Data;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerEmail).HasMaxLength(320).IsRequired();
            b.Property(x => x.CustomerPhone).HasMaxLength(32);
            b.Property(x => x.NotificationChannel).HasMaxLength(32).HasDefaultValue(NotificationChannels.Email);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.RejectionReasonCode).HasMaxLength(100);
            b.Property(x => x.RejectionReason).HasMaxLength(500);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("order_items");
            b.HasKey(x => x.Id);
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
