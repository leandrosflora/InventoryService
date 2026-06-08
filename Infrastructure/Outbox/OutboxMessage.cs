namespace InventoryService.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    private OutboxMessage()
    {
    }

    public OutboxMessage(string eventType, string payload)
    {
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required", nameof(eventType));
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Payload is required", nameof(payload));

        Id = Guid.NewGuid();
        EventType = eventType;
        Payload = payload;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsProcessed()
    {
        ProcessedAt = DateTimeOffset.UtcNow;
    }
}
