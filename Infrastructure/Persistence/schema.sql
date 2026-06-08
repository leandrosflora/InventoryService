CREATE TABLE inventory_items (
    id UUID PRIMARY KEY,
    seller_id UUID NOT NULL,
    sku_id UUID NOT NULL,
    fulfillment_center_id UUID NOT NULL,
    on_hand_quantity INTEGER NOT NULL,
    reserved_quantity INTEGER NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,

    CONSTRAINT uq_inventory_item UNIQUE (
        seller_id,
        sku_id,
        fulfillment_center_id
    ),

    CONSTRAINT ck_inventory_non_negative CHECK (
        on_hand_quantity >= 0
        AND reserved_quantity >= 0
        AND reserved_quantity <= on_hand_quantity
    )
);

CREATE INDEX idx_inventory_seller_sku
ON inventory_items (seller_id, sku_id);

CREATE INDEX idx_inventory_fc
ON inventory_items (fulfillment_center_id);

CREATE TABLE inventory_reservations (
    id UUID PRIMARY KEY,
    checkout_id UUID NOT NULL,
    seller_id UUID NOT NULL,
    idempotency_key VARCHAR(200) NOT NULL UNIQUE,
    status VARCHAR(30) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    confirmed_at TIMESTAMP WITH TIME ZONE NULL,
    released_at TIMESTAMP WITH TIME ZONE NULL
);

CREATE INDEX idx_inventory_reservations_status_expires
ON inventory_reservations (status, expires_at);

CREATE TABLE inventory_reservation_items (
    id UUID PRIMARY KEY,
    reservation_id UUID NOT NULL REFERENCES inventory_reservations(id),
    sku_id UUID NOT NULL,
    fulfillment_center_id UUID NOT NULL,
    quantity INTEGER NOT NULL
);

CREATE TABLE outbox_messages (
    id UUID PRIMARY KEY,
    event_type VARCHAR(200) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    processed_at TIMESTAMP WITH TIME ZONE NULL
);

CREATE INDEX idx_outbox_messages_processed_at
ON outbox_messages (processed_at);
