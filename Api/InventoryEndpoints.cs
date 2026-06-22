using InventoryService.Application;
using InventoryService.Contracts;

namespace InventoryService.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/inventory")
            .WithTags("Inventory");

        group.MapPost("/availability/batch", async (
            BatchAvailabilityRequest request,
            InventoryApplicationService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetAvailabilityAsync(request, cancellationToken);
            return Results.Ok(response);
        });

        group.MapGet("/{sellerId:guid}/{skuId:guid}", async (
            Guid sellerId,
            Guid skuId,
            InventoryApplicationService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetAvailabilityAsync(
                new BatchAvailabilityRequest(sellerId, [skuId]),
                cancellationToken);

            return Results.Ok(response);
        });

        group.MapPost("/reservations", async (
            CreateReservationRequest request,
            HttpContext httpContext,
            ReservationApplicationService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                idempotencyKey = httpContext.Request.Headers["x-idempotency-key"].ToString();
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest(new
                {
                    error = "Idempotency-Key or x-idempotency-key header is required"
                });
            }

            var response = await service.CreateReservationAsync(request, idempotencyKey, cancellationToken);

            return Results.Created($"/v1/inventory/reservations/{response.ReservationId}", response);
        });

        group.MapPost("/reservations/{reservationId:guid}/confirm", async (
            Guid reservationId,
            ReservationApplicationService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ConfirmReservationAsync(reservationId, cancellationToken);
            return Results.Ok(response);
        });

        group.MapPost("/reservations/{reservationId:guid}/release", async (
            Guid reservationId,
            ReservationApplicationService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ReleaseReservationAsync(reservationId, cancellationToken);
            return Results.Ok(response);
        });

        group.MapPost("/adjustments", async (
            StockAdjustmentRequest request,
            InventoryApplicationService service,
            CancellationToken cancellationToken) =>
        {
            await service.AdjustStockAsync(request, cancellationToken);
            return Results.Accepted();
        });

        return app;
    }
}
