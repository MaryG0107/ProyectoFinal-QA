using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FlashStore.Domain.Caching;
using FlashStore.Infrastructure.Caching;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace FlashStore.Api.Security;

public sealed class JwtSettings
{
    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required int ExpiresMinutes { get; init; }

    public required SymmetricSecurityKey SigningKey { get; init; }

    /// <summary>
    /// La llave del JWT debe venir de una variable de entorno (Jwt__Key) o de un secreto,
    /// nunca del repositorio. Si falta o es corta, la API no arranca.
    /// </summary>
    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key debe tener al menos 32 caracteres. Defínala con la variable de entorno Jwt__Key.");
        }

        return new JwtSettings
        {
            Issuer = configuration["Jwt:Issuer"] ?? "flashstore-api",
            Audience = configuration["Jwt:Audience"] ?? "flashstore-clients",
            ExpiresMinutes = int.TryParse(configuration["Jwt:ExpiresMinutes"], out var minutes) && minutes > 0 ? minutes : 60,
            SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    }
}

public sealed record IssuedToken(string Token, string TokenId, DateTime ExpiresAt);

public sealed class TokenService
{
    private readonly JwtSettings _settings;

    public TokenService(JwtSettings settings)
    {
        _settings = settings;
    }

    public IssuedToken Create(int userId, string email, string role)
    {
        var tokenId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_settings.ExpiresMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(_settings.SigningKey, SecurityAlgorithms.HmacSha256));

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), tokenId, expiresAt);
    }
}

public enum SessionState
{
    Active,
    Revoked,
    Unknown
}

/// <summary>
/// Sesiones en Redis: session:{jti} con el mismo TTL que el token.
/// Logout borra la llave y el token deja de funcionar aunque no haya expirado.
/// Si Redis está caído se acepta el JWT (modo degradado) y se registra la advertencia;
/// es una decisión documentada a favor de la disponibilidad.
/// </summary>
public sealed class SessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SessionStore> _logger;

    public SessionStore(IConnectionMultiplexer redis, ILogger<SessionStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task SaveAsync(string tokenId, int userId, TimeSpan ttl)
    {
        try
        {
            await _redis.GetDatabase().StringSetAsync(CacheKeys.Session(tokenId), userId, ttl);
        }
        catch (Exception ex) when (RedisCache.IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis no disponible: la sesión {TokenId} no se pudo registrar", tokenId);
        }
    }

    public async Task<SessionState> CheckAsync(string tokenId)
    {
        try
        {
            return await _redis.GetDatabase().KeyExistsAsync(CacheKeys.Session(tokenId))
                ? SessionState.Active
                : SessionState.Revoked;
        }
        catch (Exception ex) when (RedisCache.IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis no disponible: se acepta el JWT sin validar la sesión (modo degradado)");
            return SessionState.Unknown;
        }
    }

    public async Task RevokeAsync(string tokenId)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(CacheKeys.Session(tokenId));
        }
        catch (Exception ex) when (RedisCache.IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis no disponible: no se pudo cerrar la sesión {TokenId}", tokenId);
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? throw new InvalidOperationException("El token no contiene el claim 'sub'."),
            CultureInfo.InvariantCulture);

    public static string GetTokenId(this ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
        ?? throw new InvalidOperationException("El token no contiene el claim 'jti'.");

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole("admin");
}
