using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopNGo.NotificationService.Data;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SHOPNGO_NOTIFICATION_DB_MIGRATIONS_CS")
            ?? "Host=localhost;Port=5435;Database=notificationdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new NotificationDbContext(options);
    }
}
