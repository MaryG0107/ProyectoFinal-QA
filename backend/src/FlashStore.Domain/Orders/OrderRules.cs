using FlashStore.Domain.Common;

namespace FlashStore.Domain.Orders;

/// <summary>
/// Producto y cantidad solicitados por el cliente. El precio NUNCA viene del cliente:
/// siempre se toma de la base de datos (protección contra manipulación de precios).
/// </summary>
public sealed record OrderItemRequest(int ProductId, int Quantity);

public static class OrderRules
{
    public const int MaxQuantityPerItem = 10;
    public const int MaxDistinctItems = 20;
    public const int MaxIdempotencyKeyLength = 100;

    /// <summary>
    /// Valida los productos de un pedido, une las líneas repetidas del mismo producto
    /// y las ordena por ProductId (el orden fijo evita bloqueos mutuos en la base de datos).
    /// </summary>
    public static ValidationResult<IReadOnlyList<OrderItemRequest>> NormalizeItems(IEnumerable<OrderItemRequest>? items)
    {
        if (items is null)
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure("La orden debe incluir al menos un producto.");
        }

        var list = items.ToList();

        if (list.Count == 0)
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure("La orden debe incluir al menos un producto.");
        }

        if (list.Any(i => i.ProductId <= 0))
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure("Identificador de producto inválido.");
        }

        if (list.Any(i => i.Quantity <= 0))
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure("La cantidad debe ser mayor que cero.");
        }

        if (list.Any(i => i.Quantity > MaxQuantityPerItem))
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure(
                $"La cantidad máxima por producto es {MaxQuantityPerItem}.");
        }

        var merged = list
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemRequest(g.Key, g.Sum(x => x.Quantity)))
            .OrderBy(i => i.ProductId)
            .ToList();

        if (merged.Count > MaxDistinctItems)
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure(
                $"La orden no puede tener más de {MaxDistinctItems} productos distintos.");
        }

        if (merged.Any(i => i.Quantity > MaxQuantityPerItem))
        {
            return ValidationResult<IReadOnlyList<OrderItemRequest>>.Failure(
                $"La cantidad máxima por producto es {MaxQuantityPerItem}.");
        }

        return ValidationResult<IReadOnlyList<OrderItemRequest>>.Success(merged);
    }

    /// <summary>
    /// La llave de idempotencia la genera el cliente (UUID) y debe ser corta y sin caracteres especiales.
    /// </summary>
    public static bool IsValidIdempotencyKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= MaxIdempotencyKeyLength
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
}
