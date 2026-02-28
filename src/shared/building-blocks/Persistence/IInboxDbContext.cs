using Microsoft.EntityFrameworkCore;

namespace ShopNGo.BuildingBlocks.Persistence;

public interface IInboxDbContext
{
    DbSet<InboxMessage> InboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
