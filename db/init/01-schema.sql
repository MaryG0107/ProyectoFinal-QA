-- =============================================================
-- FlashStore - Esquema de base de datos (PostgreSQL 16)
-- Se ejecuta automáticamente la primera vez que arranca el
-- contenedor de PostgreSQL (docker-entrypoint-initdb.d).
-- =============================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- -------------------------------------------------------------
-- Usuarios (clientes y administradores)
-- -------------------------------------------------------------
CREATE TABLE users (
    id             SERIAL PRIMARY KEY,
    email          VARCHAR(150) NOT NULL UNIQUE,
    password_hash  VARCHAR(100) NOT NULL,
    full_name      VARCHAR(150) NOT NULL,
    role           VARCHAR(20)  NOT NULL DEFAULT 'customer'
                   CHECK (role IN ('customer', 'admin')),
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now()
);

-- -------------------------------------------------------------
-- Catálogo
-- -------------------------------------------------------------
CREATE TABLE categories (
    id    SERIAL PRIMARY KEY,
    name  VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE products (
    id           SERIAL PRIMARY KEY,
    category_id  INT           NOT NULL REFERENCES categories(id),
    name         VARCHAR(150)  NOT NULL,
    description  TEXT          NOT NULL DEFAULT '',
    price        NUMERIC(10,2) NOT NULL CHECK (price > 0),
    stock        INT           NOT NULL CHECK (stock >= 0),
    is_featured  BOOLEAN       NOT NULL DEFAULT false,
    is_active    BOOLEAN       NOT NULL DEFAULT true,
    updated_at   TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE INDEX ix_products_category ON products(category_id);

-- -------------------------------------------------------------
-- Pedidos
-- -------------------------------------------------------------
CREATE TABLE orders (
    id           UUID          PRIMARY KEY,
    user_id      INT           NOT NULL REFERENCES users(id),
    status       VARCHAR(20)   NOT NULL DEFAULT 'PENDING_PAYMENT'
                 CHECK (status IN ('PENDING_PAYMENT', 'PAID', 'PAYMENT_REJECTED', 'CANCELLED')),
    subtotal     NUMERIC(10,2) NOT NULL CHECK (subtotal >= 0),
    discount     NUMERIC(10,2) NOT NULL DEFAULT 0 CHECK (discount >= 0),
    total        NUMERIC(10,2) NOT NULL CHECK (total >= 0),
    created_at   TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE INDEX ix_orders_user ON orders(user_id);

CREATE TABLE order_items (
    id          SERIAL PRIMARY KEY,
    order_id    UUID          NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id  INT           NOT NULL REFERENCES products(id),
    quantity    INT           NOT NULL CHECK (quantity > 0),
    unit_price  NUMERIC(10,2) NOT NULL CHECK (unit_price > 0)
);

CREATE INDEX ix_order_items_order ON order_items(order_id);

-- -------------------------------------------------------------
-- Idempotencia: evita pedidos duplicados por doble clic o
-- reenvío desde la cola offline de la PWA.
-- La FK es diferida porque la llave se registra ANTES de crear
-- el pedido, para serializar las solicitudes duplicadas.
-- -------------------------------------------------------------
CREATE TABLE idempotency_keys (
    user_id     INT          NOT NULL REFERENCES users(id),
    idem_key    VARCHAR(100) NOT NULL,
    order_id    UUID         NOT NULL REFERENCES orders(id)
                DEFERRABLE INITIALLY DEFERRED,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, idem_key)
);

-- -------------------------------------------------------------
-- Pagos simulados (un solo pago por pedido)
-- -------------------------------------------------------------
CREATE TABLE payments (
    id          UUID          PRIMARY KEY,
    order_id    UUID          NOT NULL UNIQUE REFERENCES orders(id),
    amount      NUMERIC(10,2) NOT NULL CHECK (amount >= 0),
    status      VARCHAR(20)   NOT NULL CHECK (status IN ('APPROVED', 'REJECTED')),
    card_last4  VARCHAR(4)    NOT NULL,
    reason      VARCHAR(200),
    created_at  TIMESTAMPTZ   NOT NULL DEFAULT now()
);

-- -------------------------------------------------------------
-- Patrón Outbox: los eventos se guardan en la misma transacción
-- que el cambio de negocio y luego se publican en RabbitMQ.
-- Si RabbitMQ está caído, los eventos esperan aquí sin perderse.
-- -------------------------------------------------------------
CREATE TABLE outbox_messages (
    id            UUID         PRIMARY KEY,
    type          VARCHAR(50)  NOT NULL,
    routing_key   VARCHAR(100) NOT NULL,
    payload       JSONB        NOT NULL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    published_at  TIMESTAMPTZ
);

CREATE INDEX ix_outbox_pending ON outbox_messages(created_at) WHERE published_at IS NULL;

-- -------------------------------------------------------------
-- Mensajes ya procesados por cada consumidor (manejo de
-- mensajes duplicados en RabbitMQ).
-- -------------------------------------------------------------
CREATE TABLE processed_messages (
    message_id    UUID        NOT NULL,
    consumer      VARCHAR(50) NOT NULL,
    processed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (message_id, consumer)
);

-- -------------------------------------------------------------
-- Notificaciones generadas por el consumidor de notificaciones
-- -------------------------------------------------------------
CREATE TABLE notifications (
    id          SERIAL PRIMARY KEY,
    user_id     INT          NOT NULL REFERENCES users(id),
    event_type  VARCHAR(50)  NOT NULL,
    message     VARCHAR(300) NOT NULL,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX ix_notifications_user ON notifications(user_id);

-- NOTA: la tabla de cupones se agrega en una migración posterior
-- (db/migrations/), porque el proyecto exige implementar los
-- cupones al final y ejecutar pruebas de regresión.
