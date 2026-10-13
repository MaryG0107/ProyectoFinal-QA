using Dapper;
using FlashStore.Domain.Events;
using FlashStore.Domain.Payments;
using FlashStore.Infrastructure.Outbox;
using Npgsql;

namespace FlashStore.Api.Payments;

public sealed record PaymentDto(Guid PaymentId, Guid OrderId, string Status, decimal Amount, string CardLast4, string? Reason, string Message);

public enum PaymentStatus
{
    Processed,
    InvalidCard,
    NotFound,
    Forbidden,
    Conflict
}

public sealed record PaymentResult(PaymentStatus Status, PaymentDto? Payment = null, string? Error = null);

/// <summary>
/// Pago simulado. El monto SIEMPRE se toma del pedido en la base de datos (nunca del cliente).
/// El pedido se bloquea con FOR UPDATE y la tabla payments tiene UNIQUE(order_id):
/// un doble clic en "Pagar" no genera dos cobros.
/// El cambio de estado del pedido lo hace el Worker de forma asíncrona.
/// </summary>
public sealed class PaymentService
{
    private readonly NpgsqlDataSource _dataSource;

    public PaymentService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PaymentResult> PayAsync(int userId, Guid orderId, string? cardNumber, CancellationToken cancellationToken)
    {
        var simulation = PaymentSimulator.Simulate(cardNumber);

        if (!simulation.IsValidCard)
        {
            return new PaymentResult(PaymentStatus.InvalidCard, Error: simulation.Reason);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
            "SELECT id, user_id, status, total FROM orders WHERE id = @Id FOR UPDATE",
            new { Id = orderId },
            transaction,
            cancellationToken: cancellationToken));

        if (order is null)
        {
            return new PaymentResult(PaymentStatus.NotFound, Error: "Pedido no encontrado.");
        }

        // Solo el dueño del pedido puede pagarlo (protección contra IDOR).
        if (order.UserId != userId)
        {
            return new PaymentResult(PaymentStatus.Forbidden, Error: "No tiene permiso sobre este pedido.");
        }

        if (order.Status != "PENDING_PAYMENT")
        {
            return new PaymentResult(PaymentStatus.Conflict, Error: "El pedido no está pendiente de pago.");
        }

        var hasPayment = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM payments WHERE order_id = @Id)",
            new { Id = orderId },
            transaction,
            cancellationToken: cancellationToken));

        if (hasPayment)
        {
            return new PaymentResult(PaymentStatus.Conflict, Error: "El pedido ya tiene un pago registrado.");
        }

        var paymentId = Guid.NewGuid();
        var approved = simulation.Decision == PaymentDecision.Approved;
        var status = approved ? "APPROVED" : "REJECTED";

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payments (id, order_id, amount, status, card_last4, reason)
            VALUES (@Id, @OrderId, @Amount, @Status, @CardLast4, @Reason)
            """,
            new
            {
                Id = paymentId,
                OrderId = orderId,
                Amount = order.Total,
                Status = status,
                CardLast4 = simulation.Last4,
                simulation.Reason
            },
            transaction,
            cancellationToken: cancellationToken));

        if (approved)
        {
            await OutboxWriter.AddAsync(connection, transaction, EventTypes.PaymentConfirmed,
                new PaymentConfirmedEvent(orderId, order.UserId, paymentId, order.Total));
        }
        else
        {
            await OutboxWriter.AddAsync(connection, transaction, EventTypes.PaymentRejected,
                new PaymentRejectedEvent(orderId, order.UserId, paymentId, order.Total, simulation.Reason ?? "Pago rechazado."));
        }

        await transaction.CommitAsync(cancellationToken);

        return new PaymentResult(PaymentStatus.Processed, new PaymentDto(
            paymentId,
            orderId,
            status,
            order.Total,
            simulation.Last4,
            simulation.Reason,
            "El estado del pedido se actualizará de forma asíncrona cuando el Worker procese el evento."));
    }

    private sealed class OrderRow
    {
        public Guid Id { get; set; }

        public int UserId { get; set; }

        public string Status { get; set; } = string.Empty;

        public decimal Total { get; set; }
    }
}
