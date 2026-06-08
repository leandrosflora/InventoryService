namespace InventoryService.Application.Ports;

public interface IEventPublisher
{
    Task AddToOutboxAsync(string eventType, object payload, CancellationToken cancellationToken);
}
