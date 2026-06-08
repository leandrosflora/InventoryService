namespace InventoryService.Domain;

public sealed class InventoryItem
{
    public Guid Id { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid SkuId { get; private set; }
    public Guid FulfillmentCenterId { get; private set; }
    public int OnHandQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private InventoryItem()
    {
    }

    public InventoryItem(Guid sellerId, Guid skuId, Guid fulfillmentCenterId, int onHandQuantity)
    {
        if (sellerId == Guid.Empty) throw new ArgumentException("SellerId is required", nameof(sellerId));
        if (skuId == Guid.Empty) throw new ArgumentException("SkuId is required", nameof(skuId));
        if (fulfillmentCenterId == Guid.Empty) throw new ArgumentException("FulfillmentCenterId is required", nameof(fulfillmentCenterId));
        if (onHandQuantity < 0) throw new ArgumentException("On hand quantity cannot be negative", nameof(onHandQuantity));

        Id = Guid.NewGuid();
        SellerId = sellerId;
        SkuId = skuId;
        FulfillmentCenterId = fulfillmentCenterId;
        OnHandQuantity = onHandQuantity;
        ReservedQuantity = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public void AdjustOnHand(int quantityDelta)
    {
        var newQuantity = OnHandQuantity + quantityDelta;

        if (newQuantity < ReservedQuantity)
            throw new InvalidOperationException("On hand quantity cannot be lower than reserved quantity");

        OnHandQuantity = newQuantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
