-- =============================================================
-- FlashStore - Cupones y promociones (migración posterior, D-12)
-- En una base nueva se aplica sola (docker-entrypoint-initdb.d).
-- En una base existente:
--   docker exec -i flashstore-postgres psql -U flashstore -d flashstore < db/init/03-coupons.sql
-- =============================================================
CREATE TABLE IF NOT EXISTS coupons (
    code             VARCHAR(30)   PRIMARY KEY,
    description      VARCHAR(200)  NOT NULL,
    discount_percent NUMERIC(5,2)  NOT NULL CHECK (discount_percent > 0 AND discount_percent <= 100),
    max_discount     NUMERIC(10,2)          CHECK (max_discount IS NULL OR max_discount > 0),
    min_subtotal     NUMERIC(10,2) NOT NULL DEFAULT 0 CHECK (min_subtotal >= 0),
    valid_from       TIMESTAMPTZ,
    valid_until      TIMESTAMPTZ,
    is_active        BOOLEAN       NOT NULL DEFAULT TRUE,
    usage_limit      INT                    CHECK (usage_limit IS NULL OR usage_limit > 0),
    times_used       INT           NOT NULL DEFAULT 0 CHECK (times_used >= 0),
    created_at       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT coupons_code_upper CHECK (code = upper(code))
);

INSERT INTO coupons (code, description, discount_percent, max_discount, min_subtotal, valid_from, valid_until, is_active, usage_limit, times_used)
VALUES
    ('FLASH10',      '10 % de descuento en la venta flash',            10, NULL,  100, now() - interval '1 day',  now() + interval '365 days', TRUE,  NULL, 0),
    ('BIENVENIDA50', '50 % para clientes nuevos, máximo Q200',          50,  200,  500, now() - interval '1 day',  now() + interval '365 days', TRUE,  NULL, 0),
    ('EXPIRADO',     'Cupón de una campaña anterior (vencido)',         20, NULL,    0, now() - interval '60 days', now() - interval '1 day',   TRUE,  NULL, 0),
    ('INACTIVO',     'Cupón desactivado por el administrador',          15, NULL,    0, NULL,                       NULL,                       FALSE, NULL, 0),
    ('AGOTADO',      'Cupón de un solo uso ya utilizado',               30, NULL,    0, NULL,                       NULL,                       TRUE,  1,    1),
    ('NAVIDAD',      'Promoción que todavía no empieza',                25, NULL,    0, now() + interval '30 days', now() + interval '60 days', TRUE,  NULL, 0)
ON CONFLICT (code) DO NOTHING;
