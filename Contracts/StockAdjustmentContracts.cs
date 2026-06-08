namespace InventoryService.Contracts;

public sealed record StockAdjustmentRequest(
    Guid SellerId,
    Guid SkuId,
    Guid FulfillmentCenterId,
    int QuantityDelta,
    string Reason);
