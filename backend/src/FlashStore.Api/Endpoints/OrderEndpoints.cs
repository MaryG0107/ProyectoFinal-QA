using System.Security.Claims;
using Dapper;
using FlashStore.Api.Cart;
using FlashStore.Api.Common;
using FlashStore.Api.Orders;
using FlashStore.Api.Payments;
using FlashStore.Api.Security;
using FlashStore.Domain.Caching;
using FlashStore.Domain.Orders;
using FlashStore.Infrastructure.Caching;
using Npgsql;

namespace FlashStore.Api.Endpoints;

public static class OrderEndpoints
{
    /// <summary>
    /// Cuerpo de POST /api/orders. No existe campo de precio: aunque el cliente envíe
    /// "unitPrice" o "total", se ignoran y se usan los precios de PostgreSQL.
    /// </summary>
    public sealed record CreateOrderRequest(List<OrderItemRequest>? Items, bool FromCart);

    public sealed record PaymentRequest(Guid OrderId, string? CardNumber);

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Pedidos").RequireAuthorization();

        orders.MapPost("", CreateOrderAsync);
        orders.MapGet("", ListOrdersAsync);
        orders.MapGet("/{id:guid}", GetOrderAsync);
        orders.MapPost("/{id:guid}/cancel", CancelOrderAsync);

        app.MapPost("/api/payments", PayAsync).WithTags("Pagos").RequireAuthorization();

        app.MapGet("/api/notifications", ListNotificationsAsync).WithTags("Notificaciones").RequireAuthorization();

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        HttpContext http,
        ClaimsPrincipal user,
        OrderService orderService,
        CartService carts,
        RedisCache cache,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();

        if (!OrderRules.IsValidIdempotencyKey(idempotencyKey))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest,
                "El header Idempotency-Key es obligatorio: un UUID generado por el cliente para cada intento de compra.");
        }

        var userId = user.GetUserId();
        IEnumerable<OrderItemRequest>? source = request.Items;

        if (request.FromCart)
        {
            try
            {
                source = (await carts.GetRawAsync(userId)).Select(kv => new OrderItemRequest(kv.Key, kv.Value));
            }
            catch (CartUnavailableException)
            {
                return ApiResults.Error(StatusCodes.Status503ServiceUnavailable,
                    "El carrito no está disponible (Redis fuera de servicio). Envíe los productos en 'items'.");
            }
        }

        var normalized = OrderRules.NormalizeItems(source);

        if (!normalized.IsValid)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, normalized.Error!);
        }

        var result = await orderService.CreateAsync(userId, normalized.Value!, idempotencyKey, cancellationToken);

        switch (result.Status)
        {
            case CreateOrderStatus.Created:
                await cache.InvalidateAsync(CacheKeys.ForProductChange(normalized.Value!.Select(i => i.ProductId)));

                if (request.FromCart)
                {
                    try
                    {
                        await carts.ClearAsync(userId);
                    }
                    catch (CartUnavailableException)
                    {
                        // El pedido ya está creado; el carrito expirará solo por su TTL.
                    }
                }

                return Results.Created($"/api/orders/{result.Order!.Id}", result.Order);

            case CreateOrderStatus.Replayed:
                http.Response.Headers["Idempotent-Replayed"] = "true";
                return Results.Ok(result.Order);

            case CreateOrderStatus.ProductNotFound:
                return ApiResults.Error(StatusCodes.Status404NotFound, result.Error!);

            case CreateOrderStatus.OutOfStock:
                return Results.Json(new { error = result.Error, productId = result.ProductId }, statusCode: StatusCodes.Status409Conflict);

            default:
                return ApiResults.Error(StatusCodes.Status409Conflict,
                    "Ya hay una solicitud en proceso con esta Idempotency-Key. Reintente en unos segundos.");
        }
    }

    private static async Task<IResult> ListOrdersAsync(ClaimsPrincipal user, OrderService orderService, CancellationToken cancellationToken) =>
        Results.Ok(await orderService.ListByUserAsync(user.GetUserId(), cancellationToken));

    /// <summary>Un cliente solo puede ver sus pedidos; el administrador puede ver todos (control de acceso / IDOR).</summary>
    private static async Task<IResult> GetOrderAsync(Guid id, ClaimsPrincipal user, OrderService orderService, CancellationToken cancellationToken)
    {
        var order = await orderService.GetByIdAsync(id, cancellationToken);

        if (order is null)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, "Pedido no encontrado.");
        }

        if (order.UserId != user.GetUserId() && !user.IsAdmin())
        {
            return ApiResults.Error(StatusCodes.Status403Forbidden, "No tiene permiso sobre este pedido.");
        }

        return Results.Ok(order);
    }

    private static async Task<IResult> CancelOrderAsync(Guid id, ClaimsPrincipal user, OrderService orderService, CancellationToken cancellationToken)
    {
        var result = await orderService.CancelAsync(user.GetUserId(), user.IsAdmin(), id, cancellationToken);

        return result.Status switch
        {
            OrderActionStatus.Done => Results.Accepted($"/api/orders/{id}",
                new { orderId = id, status = "CANCELLED", message = "El inventario se liberará de forma asíncrona." }),
            OrderActionStatus.NotFound => ApiResults.Error(StatusCodes.Status404NotFound, result.Error!),
            OrderActionStatus.Forbidden => ApiResults.Error(StatusCodes.Status403Forbidden, result.Error!),
            _ => ApiResults.Error(StatusCodes.Status409Conflict, result.Error!)
        };
    }

    private static async Task<IResult> PayAsync(PaymentRequest request, ClaimsPrincipal user, PaymentService payments, CancellationToken cancellationToken)
    {
        if (request.OrderId == Guid.Empty)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "El campo orderId es obligatorio.");
        }

        var result = await payments.PayAsync(user.GetUserId(), request.OrderId, request.CardNumber, cancellationToken);

        return result.Status switch
        {
            PaymentStatus.Processed => Results.Accepted($"/api/orders/{request.OrderId}", result.Payment),
            PaymentStatus.InvalidCard => ApiResults.Error(StatusCodes.Status400BadRequest, result.Error!),
            PaymentStatus.NotFound => ApiResults.Error(StatusCodes.Status404NotFound, result.Error!),
            PaymentStatus.Forbidden => ApiResults.Error(StatusCodes.Status403Forbidden, result.Error!),
            _ => ApiResults.Error(StatusCodes.Status409Conflict, result.Error!)
        };
    }

    private static async Task<IResult> ListNotificationsAsync(ClaimsPrincipal user, NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var notifications = await connection.QueryAsync<NotificationDto>(
            """
            SELECT id, event_type, message, created_at
            FROM notifications
            WHERE user_id = @UserId
            ORDER BY created_at DESC
            LIMIT 50
            """,
            new { UserId = user.GetUserId() });

        return Results.Ok(notifications);
    }

    private sealed class NotificationDto
    {
        public int Id { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
