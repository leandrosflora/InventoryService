using InventoryService.Application.Ports;
using InventoryService.Contracts;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Infrastructure.Persistence;

public sealed class InventoryRepository : IInventoryRepository
{
    private readonly InventoryDbContext _dbContext;

    public InventoryRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<InventoryAvailabilityResponse>> GetAvailabilityAsync(
        Guid sellerId,
        IReadOnlyList<Guid> skuIds,
        CancellationToken cancellationToken)
    {
        return await _dbContext.InventoryItems
            .AsNoTracking()
            .Where(x => x.SellerId == sellerId)
            .Where(x => skuIds.Contains(x.SkuId))
            .Select(x => new InventoryAvailabilityResponse(
                x.SellerId,
                x.SkuId,
                x.FulfillmentCenterId,
                x.OnHandQuantity,
                x.ReservedQuantity,
                x.OnHandQuantity - x.ReservedQuantity))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryReserveAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory_items
            SET reserved_quantity = reserved_quantity + {quantity},
                updated_at = NOW()
            WHERE seller_id = {sellerId}
              AND sku_id = {skuId}
              AND fulfillment_center_id = {fulfillmentCenterId}
              AND on_hand_quantity - reserved_quantity >= {quantity}
            """, cancellationToken);

        return rowsAffected == 1;
    }

    public async Task ReleaseReservedAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory_items
            SET reserved_quantity = reserved_quantity - {quantity},
                updated_at = NOW()
            WHERE seller_id = {sellerId}
              AND sku_id = {skuId}
              AND fulfillment_center_id = {fulfillmentCenterId}
              AND reserved_quantity >= {quantity}
            """, cancellationToken);

        if (rowsAffected != 1)
            throw new InvalidOperationException($"Unable to release reserved inventory for SKU {skuId}");
    }

    public async Task ConfirmReservedAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory_items
            SET reserved_quantity = reserved_quantity - {quantity},
                on_hand_quantity = on_hand_quantity - {quantity},
                updated_at = NOW()
            WHERE seller_id = {sellerId}
              AND sku_id = {skuId}
              AND fulfillment_center_id = {fulfillmentCenterId}
              AND reserved_quantity >= {quantity}
              AND on_hand_quantity >= {quantity}
            """, cancellationToken);

        if (rowsAffected != 1)
            throw new InvalidOperationException($"Unable to confirm reserved inventory for SKU {skuId}");
    }

    public async Task AdjustOnHandAsync(
        Guid sellerId,
        Guid skuId,
        Guid fulfillmentCenterId,
        int quantityDelta,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory_items
            SET on_hand_quantity = on_hand_quantity + {quantityDelta},
                updated_at = NOW()
            WHERE seller_id = {sellerId}
              AND sku_id = {skuId}
              AND fulfillment_center_id = {fulfillmentCenterId}
              AND on_hand_quantity + {quantityDelta} >= reserved_quantity
            """, cancellationToken);

        if (rowsAffected != 1)
            throw new InvalidOperationException($"Unable to adjust inventory for SKU {skuId}");
    }
}
