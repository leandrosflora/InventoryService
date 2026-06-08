namespace InventoryService.Contracts;

public sealed record BatchAvailabilityRequest(
    Guid SellerId,
    IReadOnlyList<Guid> SkuIds);

public sealed record InventoryAvailabilityResponse(
    Guid SellerId,
    Guid SkuId,
    Guid FulfillmentCenterId,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity);
