using System.Security.Claims;
using Dapper;
using FlashStore.Api.Common;
using FlashStore.Api.Security;
using Npgsql;

namespace FlashStore.Api.Endpoints;

public static class AuthEndpoints
{
    // Hash ficticio: si el correo no existe se verifica igual contra este hash,
    // para que el tiempo de respuesta no revele qué correos están registrados.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("FlashStore-usuario-inexistente-1", 10);

    public sealed record RegisterRequest(string? Email, string? Password, string? FullName);

    public sealed record LoginRequest(string? Email, string? Password);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Autenticación");

        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", GetCurrentUserAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, NpgsqlDataSource dataSource)
    {
        var email = request.Email?.Trim().ToLowerInvariant();

        if (!InputValidation.IsValidEmail(email))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "Correo electrónico inválido.");
        }

        if (!InputValidation.IsValidPassword(request.Password))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest,
                "La contraseña debe tener entre 8 y 72 caracteres, con al menos una letra y un número.");
        }

        if (!InputValidation.IsValidName(request.FullName))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "El nombre debe tener entre 2 y 150 caracteres.");
        }

        var fullName = request.FullName!.Trim();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 11);

        await using var connection = await dataSource.OpenConnectionAsync();

        // El rol siempre es 'customer': aunque el cliente envíe "role": "admin" se ignora
        // (protección contra asignación masiva de privilegios).
        var userId = await connection.QuerySingleOrDefaultAsync<int?>(
            """
            INSERT INTO users (email, password_hash, full_name, role)
            VALUES (@Email, @PasswordHash, @FullName, 'customer')
            ON CONFLICT (email) DO NOTHING
            RETURNING id
            """,
            new { Email = email, PasswordHash = passwordHash, FullName = fullName });

        if (userId is null)
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, "El correo ya está registrado.");
        }

        return Results.Created($"/api/auth/me", new { id = userId, email, fullName, role = "customer" });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        NpgsqlDataSource dataSource,
        TokenService tokens,
        SessionStore sessions)
    {
        var email = request.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(request.Password))
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "Credenciales inválidas.");
        }

        await using var connection = await dataSource.OpenConnectionAsync();

        var user = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT id, email, password_hash, full_name, role FROM users WHERE email = @Email",
            new { Email = email });

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);

        if (user is null || !passwordOk)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "Credenciales inválidas.");
        }

        var issued = tokens.Create(user.Id, user.Email, user.Role);
        await sessions.SaveAsync(issued.TokenId, user.Id, issued.ExpiresAt - DateTime.UtcNow);

        return Results.Ok(new
        {
            token = issued.Token,
            expiresAt = issued.ExpiresAt,
            user = new { id = user.Id, email = user.Email, fullName = user.FullName, role = user.Role }
        });
    }

    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, SessionStore sessions)
    {
        await sessions.RevokeAsync(user.GetTokenId());
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(ClaimsPrincipal principal, NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var user = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT id, email, password_hash, full_name, role FROM users WHERE id = @Id",
            new { Id = principal.GetUserId() });

        return user is null
            ? ApiResults.Error(StatusCodes.Status404NotFound, "Usuario no encontrado.")
            : Results.Ok(new { id = user.Id, email = user.Email, fullName = user.FullName, role = user.Role });
    }

    private sealed class UserRow
    {
        public int Id { get; set; }

        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;
    }
}
