namespace InventoryService.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";
    public string ConsumerGroupId { get; init; } = "inventory-service";
    public KafkaTopics Topics { get; init; } = new();
}

public sealed class KafkaTopics
{
    public string InventoryCommands { get; init; } = "inventory.commands";
    public string InventoryReserved { get; init; } = "inventory.reserved";
    public string InventoryReservationConfirmed { get; init; } = "inventory.reservation.confirmed";
    public string InventoryReservationFailed { get; init; } = "inventory.reservation.failed";
    public string InventoryReservationReleased { get; init; } = "inventory.reservation.released";
}
