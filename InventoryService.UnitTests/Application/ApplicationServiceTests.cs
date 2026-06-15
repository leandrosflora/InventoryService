using InventoryService.Application;
using InventoryService.Application.Ports;
using InventoryService.Contracts;
using InventoryService.Domain;

namespace InventoryService.UnitTests.Application;

public sealed class ApplicationServiceTests
{
    [Fact]
    public async Task GetAvailabilityAsync_DeduplicatesSkuIdsBeforeCallingRepository()
    {
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var inventory = new FakeInventoryRepository();
        var service = CreateInventoryService(inventory: inventory);

        await service.GetAvailabilityAsync(new BatchAvailabilityRequest(sellerId, [skuId, skuId]), CancellationToken.None);

        Assert.Equal(sellerId, inventory.LastAvailabilitySellerId);
        Assert.Collection(inventory.LastAvailabilitySkuIds, value => Assert.Equal(skuId, value));
    }

    [Fact]
    public async Task GetAvailabilityAsync_WithEmptySkuList_DoesNotCallRepository()
    {
        var inventory = new FakeInventoryRepository();
        var service = CreateInventoryService(inventory: inventory);

        var response = await service.GetAvailabilityAsync(new BatchAvailabilityRequest(Guid.NewGuid(), []), CancellationToken.None);

        Assert.Empty(response);
        Assert.Equal(0, inventory.GetAvailabilityCalls);
    }

    [Fact]
    public async Task AdjustStockAsync_UpdatesInventoryAndPublishesOutboxEvent()
    {
        var inventory = new FakeInventoryRepository();
        var publisher = new FakeEventPublisher();
        var reservations = new FakeReservationRepository();
        var transactionRunner = new FakeTransactionRunner();
        var service = CreateInventoryService(inventory, publisher, reservations, transactionRunner);
        var request = new StockAdjustmentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, "cycle-count");

        await service.AdjustStockAsync(request, CancellationToken.None);

        Assert.Equal((request.SellerId, request.SkuId, request.FulfillmentCenterId, request.QuantityDelta), inventory.Adjustments.Single());
        Assert.Equal("InventoryAdjusted", publisher.Events.Single().EventType);
        Assert.Equal(1, reservations.SaveChangesCalls);
        Assert.Equal(1, transactionRunner.ExecuteCalls);
    }

    [Fact]
    public async Task CreateReservationAsync_WithExistingIdempotencyKey_ReturnsExistingReservationWithoutReservingAgain()
    {
        var existing = CreateReservation();
        var inventory = new FakeInventoryRepository();
        var reservations = new FakeReservationRepository { ExistingByIdempotencyKey = existing };
        var service = CreateReservationService(inventory: inventory, reservations: reservations);

        var response = await service.CreateReservationAsync(CreateReservationRequest(), existing.IdempotencyKey, CancellationToken.None);

        Assert.Equal(existing.Id, response.ReservationId);
        Assert.Equal(0, inventory.TryReserveCalls.Count);
        Assert.Empty(reservations.AddedReservations);
    }

    [Fact]
    public async Task CreateReservationAsync_WhenInventoryAvailable_ReservesPersistsAndPublishesEvent()
    {
        var inventory = new FakeInventoryRepository { TryReserveResult = true };
        var publisher = new FakeEventPublisher();
        var reservations = new FakeReservationRepository();
        var service = CreateReservationService(inventory, reservations, publisher);
        var request = CreateReservationRequest();

        var response = await service.CreateReservationAsync(request, "idem-create", CancellationToken.None);

        Assert.Equal("Pending", response.Status);
        Assert.Equal(request.CheckoutId, response.CheckoutId);
        Assert.Equal(request.SellerId, response.SellerId);
        Assert.Single(inventory.TryReserveCalls);
        Assert.Single(reservations.AddedReservations);
        Assert.Equal("InventoryReserved", publisher.Events.Single().EventType);
        Assert.Equal(1, reservations.SaveChangesCalls);
    }

    [Fact]
    public async Task CreateReservationAsync_WhenSecondItemCannotBeReserved_RollsBackPreviouslyReservedItems()
    {
        var inventory = new FakeInventoryRepository { TryReserveResults = new Queue<bool>([true, false]) };
        var service = CreateReservationService(inventory: inventory);
        var request = CreateReservationRequest(itemCount: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateReservationAsync(request, "idem-fail", CancellationToken.None));

        Assert.Equal(2, inventory.TryReserveCalls.Count);
        Assert.Single(inventory.ReleaseReservedCalls);
        Assert.Equal(request.Items[0].SkuId, inventory.ReleaseReservedCalls.Single().SkuId);
    }

    [Fact]
    public async Task ConfirmReservationAsync_ConfirmsInventoryAndPublishesEvent()
    {
        var reservation = CreateReservation();
        var inventory = new FakeInventoryRepository();
        var reservations = new FakeReservationRepository { ReservationById = reservation };
        var publisher = new FakeEventPublisher();
        var service = CreateReservationService(inventory, reservations, publisher);

        var response = await service.ConfirmReservationAsync(reservation.Id, CancellationToken.None);

        Assert.Equal("Confirmed", response.Status);
        Assert.Single(inventory.ConfirmReservedCalls);
        Assert.Equal("InventoryReservationConfirmed", publisher.Events.Single().EventType);
        Assert.Equal(1, reservations.SaveChangesCalls);
    }

    [Fact]
    public async Task ReleaseReservationAsync_WhenPending_ReleasesInventoryAndPublishesEvent()
    {
        var reservation = CreateReservation();
        var inventory = new FakeInventoryRepository();
        var reservations = new FakeReservationRepository { ReservationById = reservation };
        var publisher = new FakeEventPublisher();
        var service = CreateReservationService(inventory, reservations, publisher);

        var response = await service.ReleaseReservationAsync(reservation.Id, CancellationToken.None);

        Assert.Equal("Released", response.Status);
        Assert.Single(inventory.ReleaseReservedCalls);
        Assert.Equal("InventoryReservationReleased", publisher.Events.Single().EventType);
        Assert.Equal(1, reservations.SaveChangesCalls);
    }

    private static InventoryApplicationService CreateInventoryService(
        FakeInventoryRepository? inventory = null,
        FakeEventPublisher? publisher = null,
        FakeReservationRepository? reservations = null,
        FakeTransactionRunner? transactionRunner = null) =>
        new(inventory ?? new FakeInventoryRepository(), publisher ?? new FakeEventPublisher(), reservations ?? new FakeReservationRepository(), transactionRunner ?? new FakeTransactionRunner());

    private static ReservationApplicationService CreateReservationService(
        FakeInventoryRepository? inventory = null,
        FakeReservationRepository? reservations = null,
        FakeEventPublisher? publisher = null,
        FakeTransactionRunner? transactionRunner = null) =>
        new(inventory ?? new FakeInventoryRepository(), reservations ?? new FakeReservationRepository(), publisher ?? new FakeEventPublisher(), transactionRunner ?? new FakeTransactionRunner());

    private static CreateReservationRequest CreateReservationRequest(int itemCount = 1) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Enumerable.Range(0, itemCount)
            .Select(_ => new ReservationItemRequest(Guid.NewGuid(), Guid.NewGuid(), 1))
            .ToList());

    private static InventoryReservation CreateReservation() =>
        InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), "idem-existing", [new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), 1)]);

    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public int GetAvailabilityCalls { get; private set; }
        public Guid LastAvailabilitySellerId { get; private set; }
        public IReadOnlyList<Guid> LastAvailabilitySkuIds { get; private set; } = [];
        public bool TryReserveResult { get; set; } = true;
        public Queue<bool>? TryReserveResults { get; set; }
        public List<(Guid SellerId, Guid SkuId, Guid FulfillmentCenterId, int Quantity)> TryReserveCalls { get; } = [];
        public List<(Guid SellerId, Guid SkuId, Guid FulfillmentCenterId, int Quantity)> ReleaseReservedCalls { get; } = [];
        public List<(Guid SellerId, Guid SkuId, Guid FulfillmentCenterId, int Quantity)> ConfirmReservedCalls { get; } = [];
        public List<(Guid SellerId, Guid SkuId, Guid FulfillmentCenterId, int QuantityDelta)> Adjustments { get; } = [];

        public Task<IReadOnlyList<InventoryAvailabilityResponse>> GetAvailabilityAsync(Guid sellerId, IReadOnlyList<Guid> skuIds, CancellationToken cancellationToken)
        {
            GetAvailabilityCalls++;
            LastAvailabilitySellerId = sellerId;
            LastAvailabilitySkuIds = skuIds;
            return Task.FromResult<IReadOnlyList<InventoryAvailabilityResponse>>([]);
        }

        public Task<bool> TryReserveAsync(Guid sellerId, Guid skuId, Guid fulfillmentCenterId, int quantity, CancellationToken cancellationToken)
        {
            TryReserveCalls.Add((sellerId, skuId, fulfillmentCenterId, quantity));
            return Task.FromResult(TryReserveResults?.Dequeue() ?? TryReserveResult);
        }

        public Task ReleaseReservedAsync(Guid sellerId, Guid skuId, Guid fulfillmentCenterId, int quantity, CancellationToken cancellationToken)
        {
            ReleaseReservedCalls.Add((sellerId, skuId, fulfillmentCenterId, quantity));
            return Task.CompletedTask;
        }

        public Task ConfirmReservedAsync(Guid sellerId, Guid skuId, Guid fulfillmentCenterId, int quantity, CancellationToken cancellationToken)
        {
            ConfirmReservedCalls.Add((sellerId, skuId, fulfillmentCenterId, quantity));
            return Task.CompletedTask;
        }

        public Task AdjustOnHandAsync(Guid sellerId, Guid skuId, Guid fulfillmentCenterId, int quantityDelta, CancellationToken cancellationToken)
        {
            Adjustments.Add((sellerId, skuId, fulfillmentCenterId, quantityDelta));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReservationRepository : IReservationRepository
    {
        public InventoryReservation? ReservationById { get; set; }
        public InventoryReservation? ExistingByIdempotencyKey { get; set; }
        public List<InventoryReservation> AddedReservations { get; } = [];
        public int SaveChangesCalls { get; private set; }

        public Task<InventoryReservation?> GetByIdAsync(Guid reservationId, CancellationToken cancellationToken) => Task.FromResult(ReservationById);
        public Task<InventoryReservation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(ExistingByIdempotencyKey);
        public Task<IReadOnlyList<InventoryReservation>> FindExpiredPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InventoryReservation>>([]);
        public Task AddAsync(InventoryReservation reservation, CancellationToken cancellationToken)
        {
            AddedReservations.Add(reservation);
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEventPublisher : IEventPublisher
    {
        public List<(string EventType, object Payload)> Events { get; } = [];
        public Task AddToOutboxAsync(string eventType, object payload, CancellationToken cancellationToken)
        {
            Events.Add((eventType, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransactionRunner : ITransactionRunner
    {
        public int ExecuteCalls { get; private set; }
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            await operation(cancellationToken);
        }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return await operation(cancellationToken);
        }
    }
}
