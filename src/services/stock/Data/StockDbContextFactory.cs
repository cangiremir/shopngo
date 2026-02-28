using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopNGo.StockService.Data;

public sealed class StockDbContextFactory : IDesignTimeDbContextFactory<StockDbContext>
{
    public StockDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SHOPNGO_STOCK_DB_MIGRATIONS_CS")
            ?? "Host=localhost;Port=5434;Database=stockdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<StockDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new StockDbContext(options);
    }
}
