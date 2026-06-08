using InventoryService.Application.Ports;
using InventoryService.Contracts;

namespace InventoryService.Application;

public sealed class InventoryApplicationService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IReservationRepository _reservationRepository;
    private readonly ITransactionRunner _transactionRunner;

    public InventoryApplicationService(
        IInventoryRepository inventoryRepository,
        IEventPublisher eventPublisher,
        IReservationRepository reservationRepository,
        ITransactionRunner transactionRunner)
    {
        _inventoryRepository = inventoryRepository;
        _eventPublisher = eventPublisher;
        _reservationRepository = reservationRepository;
        _transactionRunner = transactionRunner;
    }

    public Task<IReadOnlyList<InventoryAvailabilityResponse>> GetAvailabilityAsync(
        BatchAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SellerId == Guid.Empty)
            throw new ArgumentException("SellerId is required", nameof(request));

        if (request.SkuIds.Count == 0)
            return Task.FromResult<IReadOnlyList<InventoryAvailabilityResponse>>([]);

        return _inventoryRepository.GetAvailabilityAsync(
            request.SellerId,
            request.SkuIds.Distinct().ToList(),
            cancellationToken);
    }

    public Task AdjustStockAsync(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        return _transactionRunner.ExecuteAsync(async ct =>
        {
            if (request.SellerId == Guid.Empty)
                throw new ArgumentException("SellerId is required", nameof(request));

            if (request.SkuId == Guid.Empty)
                throw new ArgumentException("SkuId is required", nameof(request));

            if (request.FulfillmentCenterId == Guid.Empty)
                throw new ArgumentException("FulfillmentCenterId is required", nameof(request));

            if (request.QuantityDelta == 0)
                throw new ArgumentException("QuantityDelta cannot be zero", nameof(request));

            await _inventoryRepository.AdjustOnHandAsync(
                request.SellerId,
                request.SkuId,
                request.FulfillmentCenterId,
                request.QuantityDelta,
                ct);

            await _eventPublisher.AddToOutboxAsync(
                "InventoryAdjusted",
                new
                {
                    request.SellerId,
                    request.SkuId,
                    request.FulfillmentCenterId,
                    request.QuantityDelta,
                    request.Reason,
                    OccurredAt = DateTimeOffset.UtcNow
                },
                ct);

            await _reservationRepository.SaveChangesAsync(ct);
        }, cancellationToken);
    }
}
