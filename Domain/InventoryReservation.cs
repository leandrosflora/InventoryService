namespace InventoryService.Domain;

public sealed class InventoryReservation
{
    public Guid Id { get; private set; }
    public Guid CheckoutId { get; private set; }
    public Guid SellerId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public List<ReservationItem> Items { get; private set; } = [];

    private InventoryReservation()
    {
    }

    public static InventoryReservation Create(Guid checkoutId, Guid sellerId, string idempotencyKey, IEnumerable<ReservationItem> items)
    {
        var itemList = items.ToList();

        if (checkoutId == Guid.Empty) throw new ArgumentException("CheckoutId is required", nameof(checkoutId));
        if (sellerId == Guid.Empty) throw new ArgumentException("SellerId is required", nameof(sellerId));
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required", nameof(idempotencyKey));
        if (itemList.Count == 0) throw new ArgumentException("Reservation must have at least one item", nameof(items));

        var now = DateTimeOffset.UtcNow;

        return new InventoryReservation
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            SellerId = sellerId,
            IdempotencyKey = idempotencyKey,
            Status = ReservationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(15),
            Items = itemList
        };
    }

    public void Confirm()
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidOperationException("Only pending reservations can be confirmed");

        if (ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Reservation expired");

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
    }

    public void Release()
    {
        if (Status is ReservationStatus.Released or ReservationStatus.Expired)
            return;

        if (Status == ReservationStatus.Confirmed)
            throw new InvalidOperationException("Confirmed reservation cannot be released directly");

        Status = ReservationStatus.Released;
        ReleasedAt = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        if (Status != ReservationStatus.Pending)
            return;

        Status = ReservationStatus.Expired;
        ReleasedAt = DateTimeOffset.UtcNow;
    }
}
