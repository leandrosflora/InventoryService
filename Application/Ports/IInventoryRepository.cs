using InventoryService.Contracts;

namespace InventoryService.Application.Ports;

public interface IInventoryRepository
{
    Task<IReadOnlyList<InventoryAvailabilityResponse>> GetAvailabilityAsync(
        Guid sellerId,
        IReadOnlyList<Guid> skuIds,
        CancellationToken cancellationToken);

    Task<bool> TryReserveAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantity,
        CancellationToken cancellationToken);

    Task ReleaseReservedAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantity,
        CancellationToken cancellationToken);

    Task ConfirmReservedAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantity,
        CancellationToken cancellationToken);

    Task AdjustOnHandAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantityDelta,
        CancellationToken cancellationToken);
}
