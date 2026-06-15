using InventoryService.Domain;

namespace InventoryService.UnitTests.Domain;

public sealed class InventoryDomainTests
{
    [Fact]
    public void InventoryItem_WithValidData_ComputesAvailableQuantity()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10);

        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void InventoryItem_WithNegativeOnHand_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InventoryItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1));
    }

    [Fact]
    public void AdjustOnHand_WhenResultIsValid_UpdatesQuantityAndTimestamp()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10);
        var previousUpdatedAt = item.UpdatedAt;

        item.AdjustOnHand(-4);

        Assert.Equal(6, item.OnHandQuantity);
        Assert.True(item.UpdatedAt >= previousUpdatedAt);
    }

    [Fact]
    public void ReservationItem_WithInvalidQuantity_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void Reservation_Create_WithValidData_StartsPendingAndExpiresInFifteenMinutes()
    {
        var before = DateTimeOffset.UtcNow;
        var checkoutId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var item = new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), 2);

        var reservation = InventoryReservation.Create(checkoutId, sellerId, "idem-1", [item]);

        Assert.Equal(checkoutId, reservation.CheckoutId);
        Assert.Equal(sellerId, reservation.SellerId);
        Assert.Equal("idem-1", reservation.IdempotencyKey);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Single(reservation.Items);
        Assert.InRange(reservation.ExpiresAt, before.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
    }

    [Fact]
    public void Reservation_Confirm_WhenPending_MarksConfirmed()
    {
        var reservation = CreateReservation();

        reservation.Confirm();

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.NotNull(reservation.ConfirmedAt);
    }

    [Fact]
    public void Reservation_Release_WhenPending_MarksReleased()
    {
        var reservation = CreateReservation();

        reservation.Release();

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.NotNull(reservation.ReleasedAt);
    }

    [Fact]
    public void Reservation_Release_WhenConfirmed_Throws()
    {
        var reservation = CreateReservation();
        reservation.Confirm();

        Assert.Throws<InvalidOperationException>(reservation.Release);
    }

    private static InventoryReservation CreateReservation() =>
        InventoryReservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            [new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), 1)]);
}
