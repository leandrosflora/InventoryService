using InventoryService.Domain;

namespace InventoryService.Application.Ports;

public interface IReservationRepository
{
    Task<InventoryReservation?> GetByIdAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<InventoryReservation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryReservation>> FindExpiredPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task AddAsync(InventoryReservation reservation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
