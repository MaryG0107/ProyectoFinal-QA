using System.Data;
using Dapper;
using FlashStore.Domain.Events;
using FlashStore.Domain.Orders;
using FlashStore.Infrastructure.Outbox;
using Npgsql;

namespace FlashStore.Api.Orders;

/// <summary>
/// Estrategia de inventario:
///  - Atomic (por defecto): UPDATE ... WHERE stock >= cantidad. Nunca hay sobreventa.
///  - Unsafe: lee el stock, espera y luego escribe el valor calculado (lost update).
///    Existe SOLO para demostrar el defecto de sobreventa en el escenario NO-GO (release rc1).
/// </summary>
public enum InventoryStrategy
{
    Atomic,
    Unsafe
}

public sealed class InventoryOptions
{
    public InventoryStrategy Strategy { get; init; } = InventoryStrategy.Atomic;

    public TimeSpan UnsafeDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    public static InventoryOptions FromConfiguration(IConfiguration configuration)
    {
        var strategy = Enum.TryParse<InventoryStrategy>(configuration["Inventory:Strategy"], ignoreCase: true, out var parsed)
            ? parsed
            : InventoryStrategy.Atomic;

        var delayMs = int.TryParse(configuration["Inventory:UnsafeDelayMs"], out var ms) ? ms : 50;

        return new InventoryOptions
        {
            Strategy = strategy,
            UnsafeDelay = TimeSpan.FromMilliseconds(Math.Clamp(delayMs, 0, 2000))
        };
    }
}

public sealed class OrderItemDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}

public sealed class OrderDto
{
    public Guid Id { get; set; }

    public int UserId { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<OrderItemDto> Items { get; set; } = [];
}

public sealed class OrderSummaryDto
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public long ItemCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum CreateOrderStatus
{
    Created,
    Replayed,
    ProductNotFound,
    OutOfStock,
    InProgress
}

public sealed record CreateOrderResult(CreateOrderStatus Status, OrderDto? Order = null, string? Error = null, int? ProductId = null);

public enum OrderActionStatus
{
    Done,
    NotFound,
    Forbidden,
    Conflict
}

public sealed record OrderActionResult(OrderActionStatus Status, string? Error = null);

public sealed class OrderService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly InventoryOptions _inventory;

    public OrderService(NpgsqlDataSource dataSource, InventoryOptions inventory)
    {
        _dataSource = dataSource;
        _inventory = inventory;
    }

    public InventoryStrategy Strategy => _inventory.Strategy;

    /// <summary>
    /// Crea un pedido de forma idempotente y sin sobreventa:
    /// 1. Si la Idempotency-Key ya existe para el usuario, devuelve el pedido original.
    /// 2. Registra la llave dentro de la transacción (dos solicitudes iguales y simultáneas se serializan).
    /// 3. Descuenta el inventario producto por producto (orden fijo por ProductId).
    /// 4. Calcula el total con precios de la base de datos.
    /// 5. Guarda pedido, líneas y eventos (outbox) en la misma transacción.
    /// </summary>
    public async Task<CreateOrderResult> CreateAsync(
        int userId,
        IReadOnlyList<OrderItemRequest> items,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var existingOrderId = await FindByIdempotencyKeyAsync(connection, userId, idempotencyKey, cancellationToken);
        if (existingOrderId is Guid replayId)
        {
            return new CreateOrderResult(CreateOrderStatus.Replayed, await GetByIdAsync(connection, replayId, cancellationToken));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var orderId = Guid.NewGuid();

        var claimed = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO idempotency_keys (user_id, idem_key, order_id)
            VALUES (@UserId, @Key, @OrderId)
            ON CONFLICT (user_id, idem_key) DO NOTHING
            """,
            new { UserId = userId, Key = idempotencyKey, OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken));

        if (claimed == 0)
        {
            // Otra solicitud con la misma llave terminó primero.
            await transaction.RollbackAsync(cancellationToken);

            var winnerId = await FindByIdempotencyKeyAsync(connection, userId, idempotencyKey, cancellationToken);
            return winnerId is Guid id
                ? new CreateOrderResult(CreateOrderStatus.Replayed, await GetByIdAsync(connection, id, cancellationToken))
                : new CreateOrderResult(CreateOrderStatus.InProgress);
        }

        var lines = new List<PricedLine>();

        foreach (var item in items)
        {
            var reservation = _inventory.Strategy == InventoryStrategy.Atomic
                ? await ReserveAtomicAsync(connection, transaction, item, cancellationToken)
                : await ReserveUnsafeAsync(connection, transaction, item, cancellationToken);

            if (reservation.Status == ReservationStatus.NotFound)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateOrderResult(CreateOrderStatus.ProductNotFound,
                    Error: $"El producto {item.ProductId} no existe o no está disponible.", ProductId: item.ProductId);
            }

            if (reservation.Status == ReservationStatus.OutOfStock)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateOrderResult(CreateOrderStatus.OutOfStock,
                    Error: "Stock insuficiente para completar el pedido.", ProductId: item.ProductId);
            }

            lines.Add(new PricedLine(item.ProductId, item.Quantity, reservation.UnitPrice));
        }

        var totals = PricingCalculator.Calculate(lines);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO orders (id, user_id, status, subtotal, discount, total)
            VALUES (@Id, @UserId, 'PENDING_PAYMENT', @Subtotal, @Discount, @Total)
            """,
            new { Id = orderId, UserId = userId, totals.Subtotal, totals.Discount, totals.Total },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO order_items (order_id, product_id, quantity, unit_price)
                VALUES (@OrderId, @ProductId, @Quantity, @UnitPrice)
                """,
                new { OrderId = orderId, line.ProductId, line.Quantity, line.UnitPrice },
                transaction,
                cancellationToken: cancellationToken));
        }

        var eventLines = lines.Select(l => new EventLine(l.ProductId, l.Quantity, l.UnitPrice)).ToList();

        await OutboxWriter.AddAsync(connection, transaction, EventTypes.OrderCreated,
            new OrderCreatedEvent(orderId, userId, totals.Total, eventLines));

        await OutboxWriter.AddAsync(connection, transaction, EventTypes.InventoryReserved,
            new InventoryReservedEvent(orderId, userId, eventLines));

        await transaction.CommitAsync(cancellationToken);

        return new CreateOrderResult(CreateOrderStatus.Created, await GetByIdAsync(connection, orderId, cancellationToken));
    }

    public async Task<OrderDto?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await GetByIdAsync(connection, orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> ListByUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var orders = await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(
            """
            SELECT o.id, o.status, o.total, o.created_at,
                   COALESCE(SUM(oi.quantity), 0) AS item_count
            FROM orders o
            LEFT JOIN order_items oi ON oi.order_id = o.id
            WHERE o.user_id = @UserId
            GROUP BY o.id
            ORDER BY o.created_at DESC
            LIMIT 100
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return orders.ToList();
    }

    /// <summary>Cancela un pedido pendiente de pago. El Worker devuelve el stock al recibir OrderCancelled.</summary>
    public async Task<OrderActionResult> CancelAsync(int userId, bool isAdmin, Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var order = await connection.QuerySingleOrDefaultAsync<OrderLockRow>(new CommandDefinition(
            "SELECT id, user_id, status FROM orders WHERE id = @Id FOR UPDATE",
            new { Id = orderId },
            transaction,
            cancellationToken: cancellationToken));

        if (order is null)
        {
            return new OrderActionResult(OrderActionStatus.NotFound, "Pedido no encontrado.");
        }

        if (order.UserId != userId && !isAdmin)
        {
            return new OrderActionResult(OrderActionStatus.Forbidden, "No tiene permiso sobre este pedido.");
        }

        if (order.Status != "PENDING_PAYMENT")
        {
            return new OrderActionResult(OrderActionStatus.Conflict, "Solo se pueden cancelar pedidos pendientes de pago.");
        }

        var hasPayment = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM payments WHERE order_id = @Id)",
            new { Id = orderId },
            transaction,
            cancellationToken: cancellationToken));

        if (hasPayment)
        {
            return new OrderActionResult(OrderActionStatus.Conflict, "El pedido ya tiene un pago registrado.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE orders SET status = 'CANCELLED', updated_at = now() WHERE id = @Id",
            new { Id = orderId },
            transaction,
            cancellationToken: cancellationToken));

        await OutboxWriter.AddAsync(connection, transaction, EventTypes.OrderCancelled,
            new OrderCancelledEvent(orderId, order.UserId, isAdmin && order.UserId != userId
                ? "Cancelado por un administrador"
                : "Cancelado por el cliente"));

        await transaction.CommitAsync(cancellationToken);
        return new OrderActionResult(OrderActionStatus.Done);
    }

    private static async Task<Guid?> FindByIdempotencyKeyAsync(
        NpgsqlConnection connection,
        int userId,
        string key,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT order_id FROM idempotency_keys WHERE user_id = @UserId AND idem_key = @Key",
            new { UserId = userId, Key = key },
            cancellationToken: cancellationToken));

    /// <summary>Descuento atómico: PostgreSQL bloquea la fila y solo descuenta si alcanza el stock.</summary>
    private static async Task<Reservation> ReserveAtomicAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        OrderItemRequest item,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<StockRow>(new CommandDefinition(
            """
            UPDATE products
            SET stock = stock - @Quantity, updated_at = now()
            WHERE id = @ProductId AND is_active AND stock >= @Quantity
            RETURNING price, stock
            """,
            new { item.ProductId, item.Quantity },
            transaction,
            cancellationToken: cancellationToken));

        if (row is not null)
        {
            return new Reservation(ReservationStatus.Reserved, row.Price);
        }

        return await ProductExistsAsync(connection, transaction, item.ProductId, cancellationToken)
            ? new Reservation(ReservationStatus.OutOfStock, 0m)
            : new Reservation(ReservationStatus.NotFound, 0m);
    }

    /// <summary>
    /// DEFECTO INTENCIONAL (solo para el escenario NO-GO): lectura y escritura separadas.
    /// Varias transacciones leen el mismo stock y todas escriben el mismo valor → sobreventa.
    /// </summary>
    private async Task<Reservation> ReserveUnsafeAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        OrderItemRequest item,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<StockRow>(new CommandDefinition(
            "SELECT price, stock FROM products WHERE id = @ProductId AND is_active",
            new { item.ProductId },
            transaction,
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return new Reservation(ReservationStatus.NotFound, 0m);
        }

        if (row.Stock < item.Quantity)
        {
            return new Reservation(ReservationStatus.OutOfStock, 0m);
        }

        await Task.Delay(_inventory.UnsafeDelay, cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE products SET stock = @NewStock, updated_at = now() WHERE id = @ProductId",
            new { NewStock = row.Stock - item.Quantity, item.ProductId },
            transaction,
            cancellationToken: cancellationToken));

        return new Reservation(ReservationStatus.Reserved, row.Price);
    }

    private static Task<bool> ProductExistsAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        int productId,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM products WHERE id = @ProductId AND is_active)",
            new { ProductId = productId },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<OrderDto?> GetByIdAsync(NpgsqlConnection connection, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await connection.QuerySingleOrDefaultAsync<OrderDto>(new CommandDefinition(
            """
            SELECT id, user_id, status, subtotal, discount, total, created_at, updated_at
            FROM orders
            WHERE id = @Id
            """,
            new { Id = orderId },
            cancellationToken: cancellationToken));

        if (order is null)
        {
            return null;
        }

        order.Items = (await connection.QueryAsync<OrderItemDto>(new CommandDefinition(
            """
            SELECT oi.product_id, p.name AS product_name, oi.quantity, oi.unit_price,
                   (oi.quantity * oi.unit_price) AS line_total
            FROM order_items oi
            JOIN products p ON p.id = oi.product_id
            WHERE oi.order_id = @Id
            ORDER BY oi.product_id
            """,
            new { Id = orderId },
            cancellationToken: cancellationToken))).ToList();

        return order;
    }

    private enum ReservationStatus
    {
        Reserved,
        NotFound,
        OutOfStock
    }

    private sealed record Reservation(ReservationStatus Status, decimal UnitPrice);

    private sealed class StockRow
    {
        public decimal Price { get; set; }

        public int Stock { get; set; }
    }

    private sealed class OrderLockRow
    {
        public Guid Id { get; set; }

        public int UserId { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
