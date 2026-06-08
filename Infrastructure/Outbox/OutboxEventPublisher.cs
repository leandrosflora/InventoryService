using System.Text.Json;
using InventoryService.Application.Ports;
using InventoryService.Infrastructure.Persistence;

namespace InventoryService.Infrastructure.Outbox;

public sealed class OutboxEventPublisher : IEventPublisher
{
    private readonly InventoryDbContext _dbContext;

    public OutboxEventPublisher(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddToOutboxAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        var message = new OutboxMessage(eventType, JsonSerializer.Serialize(payload));
        await _dbContext.OutboxMessages.AddAsync(message, cancellationToken);
    }
}
