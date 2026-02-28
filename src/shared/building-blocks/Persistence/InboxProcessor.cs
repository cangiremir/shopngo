using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ShopNGo.BuildingBlocks.Persistence;

public sealed class InboxProcessor<TDbContext>(ILogger<InboxProcessor<TDbContext>> logger)
    where TDbContext : DbContext, IInboxDbContext
{
    public async Task<bool> BeginAsync(TDbContext db, string consumer, string messageId, string routingKey, CancellationToken ct)
    {
        var existing = await db.InboxMessages
            .SingleOrDefaultAsync(x => x.Consumer == consumer && x.MessageId == messageId, ct);

        if (existing is not null)
        {
            if (existing.ProcessedAtUtc is not null)
            {
                logger.LogInformation("Skipping duplicate processed message {MessageId} for {Consumer}", messageId, consumer);
                return false;
            }

            // Retry path: row was created by an earlier failed attempt and not marked complete.
            logger.LogInformation("Reprocessing previously failed message {MessageId} for {Consumer}", messageId, consumer);
            return true;
        }

        db.InboxMessages.Add(new InboxMessage
        {
            Consumer = consumer,
            MessageId = messageId,
            RoutingKey = routingKey
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent insert by another replica for the same (Consumer, MessageId).
            // Treat this delivery as duplicate/in-flight and let the winner continue processing.
            logger.LogInformation(
                "Detected concurrent inbox insert for message {MessageId} and consumer {Consumer}; skipping duplicate delivery",
                messageId,
                consumer);
            return false;
        }

        return true;
    }

    public async Task CompleteAsync(TDbContext db, string consumer, string messageId, CancellationToken ct)
    {
        var row = await db.InboxMessages.SingleAsync(x => x.Consumer == consumer && x.MessageId == messageId, ct);
        if (row.ProcessedAtUtc is null)
        {
            row.ProcessedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return true;
        }

        var message = ex.InnerException?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }
}
