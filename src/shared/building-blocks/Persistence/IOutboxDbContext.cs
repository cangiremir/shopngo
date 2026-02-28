using Microsoft.EntityFrameworkCore;

namespace ShopNGo.BuildingBlocks.Persistence;

public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
