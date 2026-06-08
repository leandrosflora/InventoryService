namespace InventoryService.Contracts;

public sealed record CreateReservationRequest(
    Guid CheckoutId,
    Guid SellerId,
    IReadOnlyList<ReservationItemRequest> Items);

public sealed record ReservationItemRequest(
    Guid SkuId,
    Guid FulfillmentCenterId,
    int Quantity);

public sealed record ReservationResponse(
    Guid ReservationId,
    Guid CheckoutId,
    Guid SellerId,
    string Status,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ReservationItemResponse> Items);

public sealed record ReservationItemResponse(
    Guid SkuId,
    Guid FulfillmentCenterId,
    int Quantity);
