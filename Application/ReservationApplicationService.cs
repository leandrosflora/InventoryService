using InventoryService.Application.Ports;
using InventoryService.Contracts;
using InventoryService.Domain;

namespace InventoryService.Application;

public sealed class ReservationApplicationService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITransactionRunner _transactionRunner;

    public ReservationApplicationService(
        IInventoryRepository inventoryRepository,
        IReservationRepository reservationRepository,
        IEventPublisher eventPublisher,
        ITransactionRunner transactionRunner)
    {
        _inventoryRepository = inventoryRepository;
        _reservationRepository = reservationRepository;
        _eventPublisher = eventPublisher;
        _transactionRunner = transactionRunner;
    }

    public Task<ReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _transactionRunner.ExecuteAsync(async ct =>
        {
            if (request.Items.Count == 0)
                throw new ArgumentException("Reservation must have at least one item", nameof(request));

            var existing = await _reservationRepository.FindByIdempotencyKeyAsync(idempotencyKey, ct);

            if (existing is not null)
                return Map(existing);

            var reservationItems = request.Items
                .Select(x => new ReservationItem(x.SkuId, x.FulfillmentCenterId, x.Quantity))
                .ToList();

            var successfullyReservedItems = new List<ReservationItem>();

            foreach (var item in reservationItems)
            {
                var reserved = await _inventoryRepository.TryReserveAsync(
                    request.SellerId,
                    item.SkuId,
                    item.FulfillmentCenterId,
                    item.Quantity,
                    ct);

                if (!reserved)
                {
                    await RollbackReservedItemsAsync(request.SellerId, successfullyReservedItems, ct);
                    throw new InvalidOperationException($"Insufficient inventory for SKU {item.SkuId}");
                }

                successfullyReservedItems.Add(item);
            }

            var reservation = InventoryReservation.Create(
                request.CheckoutId,
                request.SellerId,
                idempotencyKey,
                reservationItems);

            await _reservationRepository.AddAsync(reservation, ct);

            await _eventPublisher.AddToOutboxAsync(
                "InventoryReserved",
                new
                {
                    reservation.Id,
                    reservation.CheckoutId,
                    reservation.SellerId,
                    reservation.ExpiresAt,
                    Items = reservation.Items.Select(x => new
                    {
                        x.SkuId,
                        x.FulfillmentCenterId,
                        x.Quantity
                    })
                },
                ct);

            await _reservationRepository.SaveChangesAsync(ct);

            return Map(reservation);
        }, cancellationToken);
    }

    public Task<ReservationResponse> ConfirmReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var reservation = await _reservationRepository.GetByIdAsync(reservationId, ct);

            if (reservation is null)
                throw new InvalidOperationException("Reservation not found");

            reservation.Confirm();

            foreach (var item in reservation.Items)
            {
                await _inventoryRepository.ConfirmReservedAsync(
                    reservation.SellerId,
                    item.SkuId,
                    item.FulfillmentCenterId,
                    item.Quantity,
                    ct);
            }

            await _eventPublisher.AddToOutboxAsync(
                "InventoryReservationConfirmed",
                new
                {
                    reservation.Id,
                    reservation.CheckoutId,
                    reservation.SellerId,
                    ConfirmedAt = DateTimeOffset.UtcNow
                },
                ct);

            await _reservationRepository.SaveChangesAsync(ct);

            return Map(reservation);
        }, cancellationToken);
    }

    public Task<ReservationResponse> ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var reservation = await _reservationRepository.GetByIdAsync(reservationId, ct);

            if (reservation is null)
                throw new InvalidOperationException("Reservation not found");

            var wasPending = reservation.Status == ReservationStatus.Pending;
            reservation.Release();

            if (wasPending)
            {
                foreach (var item in reservation.Items)
                {
                    await _inventoryRepository.ReleaseReservedAsync(
                        reservation.SellerId,
                        item.SkuId,
                        item.FulfillmentCenterId,
                        item.Quantity,
                        ct);
                }
            }

            await _eventPublisher.AddToOutboxAsync(
                "InventoryReservationReleased",
                new
                {
                    reservation.Id,
                    reservation.CheckoutId,
                    reservation.SellerId,
                    ReleasedAt = DateTimeOffset.UtcNow
                },
                ct);

            await _reservationRepository.SaveChangesAsync(ct);

            return Map(reservation);
        }, cancellationToken);
    }

    private async Task RollbackReservedItemsAsync(Guid sellerId, IReadOnlyList<ReservationItem> reservedItems, CancellationToken cancellationToken)
    {
        foreach (var item in reservedItems)
        {
            await _inventoryRepository.ReleaseReservedAsync(
                sellerId,
                item.SkuId,
                item.FulfillmentCenterId,
                item.Quantity,
                cancellationToken);
        }
    }

    private static ReservationResponse Map(InventoryReservation reservation)
    {
        return new ReservationResponse(
            reservation.Id,
            reservation.CheckoutId,
            reservation.SellerId,
            reservation.Status.ToString(),
            reservation.ExpiresAt,
            reservation.Items.Select(x => new ReservationItemResponse(
                x.SkuId,
                x.FulfillmentCenterId,
                x.Quantity)).ToList());
    }
}
