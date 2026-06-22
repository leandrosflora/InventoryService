using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using InventoryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using InventoryService.Infrastructure.Messaging;

namespace InventoryService.Infrastructure.Outbox;

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var producerConfig = new ProducerConfig { BootstrapServers = _options.BootstrapServers };

        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(producer, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox dispatch cycle failed");
            }
        }
    }

    private async Task DispatchBatchAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var messages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            var envelope = new CanonicalEnvelope(
                Guid.NewGuid(),
                message.EventType,
                "1.0",
                message.CreatedAt,
                Guid.NewGuid().ToString(),
                "inventory-service",
                JsonSerializer.Deserialize<JsonElement>(message.Payload));

            var json = JsonSerializer.Serialize(envelope);

            await producer.ProduceAsync(
                message.EventType,
                new Message<string, string> { Key = message.Id.ToString(), Value = json },
                cancellationToken);

            message.MarkAsProcessed();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal sealed record CanonicalEnvelope(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("payload")] JsonElement Payload);
