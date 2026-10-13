using System.Security.Claims;
using FlashStore.Api.Cart;
using FlashStore.Api.Common;
using FlashStore.Api.Security;

namespace FlashStore.Api.Endpoints;

public static class CartEndpoints
{
    public sealed record AddItemRequest(int ProductId, int Quantity);

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cart").WithTags("Carrito").RequireAuthorization();

        group.MapPost("", GetOrCreateAsync);
        group.MapGet("", GetOrCreateAsync);
        group.MapPost("/items", AddItemAsync);
        group.MapDelete("/items/{productId:int}", RemoveItemAsync);
        group.MapDelete("", ClearAsync);

        return app;
    }

    private static Task<IResult> GetOrCreateAsync(ClaimsPrincipal user, CartService carts) =>
        Execute(async () => Results.Ok(await carts.GetViewAsync(user.GetUserId())));

    private static Task<IResult> AddItemAsync(AddItemRequest request, ClaimsPrincipal user, CartService carts) =>
        Execute(async () =>
        {
            var result = await carts.AddItemAsync(user.GetUserId(), request.ProductId, request.Quantity);

            return result.Status switch
            {
                CartAddStatus.Added => Results.Ok(result.Cart),
                CartAddStatus.ProductNotFound => ApiResults.Error(StatusCodes.Status404NotFound, result.Error!),
                CartAddStatus.OutOfStock => ApiResults.Error(StatusCodes.Status409Conflict, result.Error!),
                _ => ApiResults.Error(StatusCodes.Status400BadRequest, result.Error!)
            };
        });

    private static Task<IResult> RemoveItemAsync(int productId, ClaimsPrincipal user, CartService carts) =>
        Execute(async () => Results.Ok(await carts.RemoveItemAsync(user.GetUserId(), productId)));

    private static Task<IResult> ClearAsync(ClaimsPrincipal user, CartService carts) =>
        Execute(async () =>
        {
            await carts.ClearAsync(user.GetUserId());
            return Results.NoContent();
        });

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CartUnavailableException)
        {
            return ApiResults.Error(StatusCodes.Status503ServiceUnavailable,
                "El carrito no está disponible temporalmente (Redis fuera de servicio). Puede comprar enviando los productos directamente al crear el pedido.");
        }
    }
}
