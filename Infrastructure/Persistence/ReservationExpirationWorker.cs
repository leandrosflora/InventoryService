using InventoryService.Application.Ports;

namespace InventoryService.Infrastructure.Persistence;

public sealed class ReservationExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationExpirationWorker> _logger;

    public ReservationExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<ReservationExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExpireReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire inventory reservations");
            }
        }
    }

    private async Task ExpireReservationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var transactionRunner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
        var reservationRepository = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var inventoryRepository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        await transactionRunner.ExecuteAsync(async ct =>
        {
            var expiredReservations = await reservationRepository.FindExpiredPendingAsync(
                DateTimeOffset.UtcNow,
                limit: 100,
                ct);

            foreach (var reservation in expiredReservations)
            {
                reservation.Expire();

                foreach (var item in reservation.Items)
                {
                    await inventoryRepository.ReleaseReservedAsync(
                        reservation.SellerId,
                        item.SkuId,
                        item.FulfillmentCenterId,
                        item.Quantity,
                        ct);
                }

                await eventPublisher.AddToOutboxAsync(
                    "InventoryReservationExpired",
                    new
                    {
                        reservation.Id,
                        reservation.CheckoutId,
                        reservation.SellerId,
                        reservation.ExpiresAt
                    },
                    ct);
            }

            await reservationRepository.SaveChangesAsync(ct);
        }, cancellationToken);
    }
}
