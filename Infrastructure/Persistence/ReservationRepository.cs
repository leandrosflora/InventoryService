using InventoryService.Application.Ports;
using InventoryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Infrastructure.Persistence;

public sealed class ReservationRepository : IReservationRepository
{
    private readonly InventoryDbContext _dbContext;

    public ReservationRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<InventoryReservation?> GetByIdAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return _dbContext.Reservations
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == reservationId, cancellationToken);
    }

    public Task<InventoryReservation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _dbContext.Reservations
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryReservation>> FindExpiredPendingAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Reservations
            .Include(x => x.Items)
            .Where(x => x.Status == ReservationStatus.Pending)
            .Where(x => x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(InventoryReservation reservation, CancellationToken cancellationToken)
    {
        await _dbContext.Reservations.AddAsync(reservation, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
