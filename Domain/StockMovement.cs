namespace InventoryService.Domain;

public sealed class StockMovement
{
    public Guid Id { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid SkuId { get; private set; }
    public Guid FulfillmentCenterId { get; private set; }
    public int QuantityDelta { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private StockMovement()
    {
    }

    public StockMovement(Guid sellerId, Guid skuId, Guid fulfillmentCenterId, int quantityDelta, string reason)
    {
        if (sellerId == Guid.Empty) throw new ArgumentException("SellerId is required", nameof(sellerId));
        if (skuId == Guid.Empty) throw new ArgumentException("SkuId is required", nameof(skuId));
        if (fulfillmentCenterId == Guid.Empty) throw new ArgumentException("FulfillmentCenterId is required", nameof(fulfillmentCenterId));
        if (quantityDelta == 0) throw new ArgumentException("Quantity delta cannot be zero", nameof(quantityDelta));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required", nameof(reason));

        Id = Guid.NewGuid();
        SellerId = sellerId;
        SkuId = skuId;
        FulfillmentCenterId = fulfillmentCenterId;
        QuantityDelta = quantityDelta;
        Reason = reason;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
