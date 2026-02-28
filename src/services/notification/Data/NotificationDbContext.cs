using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.NotificationService.Domain;

namespace ShopNGo.NotificationService.Data;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IInboxDbContext
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationLog>(b =>
        {
            b.ToTable("notification_logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Target).HasMaxLength(320);
            b.Property(x => x.Channel).HasMaxLength(32);
            b.Property(x => x.Template).HasMaxLength(100);
            b.Property(x => x.Status).HasMaxLength(32);
            b.Property(x => x.ErrorCode).HasMaxLength(100);
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasIndex(x => new { x.Consumer, x.MessageId }).IsUnique();
        });
    }
}
