using InventoryService.Domain;
using InventoryService.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Infrastructure.Persistence;

public sealed class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items", table => table.HasCheckConstraint(
                "ck_inventory_non_negative",
                "on_hand_quantity >= 0 AND reserved_quantity >= 0 AND reserved_quantity <= on_hand_quantity"));
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SellerId).HasColumnName("seller_id").IsRequired();
            entity.Property(x => x.SkuId).HasColumnName("sku_id").IsRequired();
            entity.Property(x => x.FulfillmentCenterId).HasColumnName("fulfillment_center_id").IsRequired();
            entity.Property(x => x.OnHandQuantity).HasColumnName("on_hand_quantity").IsRequired();
            entity.Property(x => x.ReservedQuantity).HasColumnName("reserved_quantity").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Ignore(x => x.AvailableQuantity);

            entity.HasIndex(x => new { x.SellerId, x.SkuId, x.FulfillmentCenterId })
                .IsUnique();
            entity.HasIndex(x => new { x.SellerId, x.SkuId });
            entity.HasIndex(x => x.FulfillmentCenterId);

        });

        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.ToTable("inventory_reservations");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CheckoutId).HasColumnName("checkout_id").IsRequired();
            entity.Property(x => x.SellerId).HasColumnName("seller_id").IsRequired();
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            entity.Property(x => x.ReleasedAt).HasColumnName("released_at");

            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new { x.Status, x.ExpiresAt });

            entity.OwnsMany(x => x.Items, item =>
            {
                item.ToTable("inventory_reservation_items");
                item.WithOwner().HasForeignKey("reservation_id");
                item.Property<Guid>("reservation_id").HasColumnName("reservation_id");
                item.HasKey(x => x.Id);
                item.Property(x => x.Id).HasColumnName("id");
                item.Property(x => x.SkuId).HasColumnName("sku_id").IsRequired();
                item.Property(x => x.FulfillmentCenterId).HasColumnName("fulfillment_center_id").IsRequired();
                item.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
            });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");

            entity.HasIndex(x => x.ProcessedAt);
        });
    }
}
