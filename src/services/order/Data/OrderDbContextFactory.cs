using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopNGo.OrderService.Data;

public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SHOPNGO_ORDER_DB_MIGRATIONS_CS")
            ?? "Host=localhost;Port=5433;Database=orderdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderDbContext(options);
    }
}
