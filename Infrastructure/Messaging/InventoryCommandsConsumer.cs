using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using InventoryService.Application;
using InventoryService.Application.Ports;
using InventoryService.Contracts;
using InventoryService.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace InventoryService.Infrastructure.Messaging;

public sealed class InventoryCommandsConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<InventoryCommandsConsumer> _logger;

    public InventoryCommandsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<InventoryCommandsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.Topics.InventoryCommands);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var discriminator = JsonSerializer.Deserialize<CommandDiscriminator>(result.Message.Value, JsonOptions);

                if (discriminator is null)
                {
                    consumer.Commit(result);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ReservationApplicationService>();

                switch (discriminator.CommandType)
                {
                    case "ReserveInventory":
                    {
                        var cmd = JsonSerializer.Deserialize<ReserveInventoryCommand>(result.Message.Value, JsonOptions);
                        if (cmd is not null)
                        {
                            var idempotencyKey = $"reserve:{cmd.OrderId}";
                            var request = new CreateReservationRequest(
                                cmd.OrderId,
                                cmd.SellerId,
                                cmd.Items.Select(i => new ReservationItemRequest(i.SkuId, i.FulfillmentCenterId, i.Quantity)).ToList());
                            try
                            {
                                await service.CreateReservationAsync(request, idempotencyKey, stoppingToken);
                            }
                            catch (InvalidOperationException ex)
                            {
                                using var failScope = _scopeFactory.CreateScope();
                                var publisher = failScope.ServiceProvider.GetRequiredService<IEventPublisher>();
                                var dbCtx = failScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                                await publisher.AddToOutboxAsync(
                                    "inventory.reservation.failed",
                                    new InventoryReservationFailedPayload(cmd.OrderId, ex.Message),
                                    stoppingToken);
                                await dbCtx.SaveChangesAsync(stoppingToken);
                                _logger.LogWarning("Inventory reservation failed for order {OrderId}: {Reason}", cmd.OrderId, ex.Message);
                            }
                        }
                        break;
                    }
                    case "ConfirmInventoryReservation":
                    {
                        var cmd = JsonSerializer.Deserialize<ConfirmReservationCommand>(result.Message.Value, JsonOptions);
                        if (cmd is not null)
                            await service.ConfirmReservationAsync(cmd.ReservationId, stoppingToken);
                        break;
                    }
                    case "ReleaseInventoryReservation":
                    {
                        var cmd = JsonSerializer.Deserialize<ReleaseReservationCommand>(result.Message.Value, JsonOptions);
                        if (cmd is not null)
                            await service.ReleaseReservationAsync(cmd.ReservationId, stoppingToken);
                        break;
                    }
                    default:
                        _logger.LogWarning("Unknown inventory command type: {CommandType}", discriminator.CommandType);
                        break;
                }

                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume topic {Topic}", _options.Topics.InventoryCommands);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }
}

internal sealed record CommandDiscriminator(
    [property: JsonPropertyName("commandType")] string CommandType);

internal sealed record ReserveInventoryCommand(
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("items")] IReadOnlyList<ReserveInventoryItem> Items);

internal sealed record ReserveInventoryItem(
    [property: JsonPropertyName("skuId")] Guid SkuId,
    [property: JsonPropertyName("fulfillmentCenterId")] Guid FulfillmentCenterId,
    [property: JsonPropertyName("quantity")] int Quantity);

internal sealed record ConfirmReservationCommand(
    [property: JsonPropertyName("reservationId")] Guid ReservationId);

internal sealed record ReleaseReservationCommand(
    [property: JsonPropertyName("reservationId")] Guid ReservationId);
