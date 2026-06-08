namespace InventoryService.Domain;

public sealed class ReservationItem
{
    public Guid Id { get; private set; }
    public Guid SkuId { get; private set; }
    public Guid FulfillmentCenterId { get; private set; }
    public int Quantity { get; private set; }

    private ReservationItem()
    {
    }

    public ReservationItem(Guid skuId, Guid fulfillmentCenterId, int quantity)
    {
        if (skuId == Guid.Empty) throw new ArgumentException("SkuId is required", nameof(skuId));
        if (fulfillmentCenterId == Guid.Empty) throw new ArgumentException("FulfillmentCenterId is required", nameof(fulfillmentCenterId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

        Id = Guid.NewGuid();
        SkuId = skuId;
        FulfillmentCenterId = fulfillmentCenterId;
        Quantity = quantity;
    }
}
