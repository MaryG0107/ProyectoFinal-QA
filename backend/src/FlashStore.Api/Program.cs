using System.Threading.RateLimiting;
using FlashStore.Api.Cart;
using FlashStore.Api.Endpoints;
using FlashStore.Api.Orders;
using FlashStore.Api.Payments;
using FlashStore.Api.Security;
using FlashStore.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Infraestructura: PostgreSQL, Redis, RabbitMQ y despachador del outbox
// ---------------------------------------------------------------------------
builder.Services.AddFlashStoreInfrastructure(builder.Configuration, "flashstore-api");

// ---------------------------------------------------------------------------
// Servicios de la aplicación
// ---------------------------------------------------------------------------
var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration);
var inventoryOptions = InventoryOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton(inventoryOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<CartService>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<PaymentService>();

// ---------------------------------------------------------------------------
// Seguridad: JWT + validación de sesión en Redis + roles
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSettings.SigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var tokenId = context.Principal?.FindFirst("jti")?.Value;

                if (string.IsNullOrEmpty(tokenId))
                {
                    context.Fail("El token no tiene identificador de sesión.");
                    return;
                }

                var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionStore>();

                if (await sessions.CheckAsync(tokenId) == SessionState.Revoked)
                {
                    context.Fail("La sesión fue cerrada o expiró.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("admin", policy => policy.RequireAuthenticatedUser().RequireRole("admin")));

// Límite de intentos de login por IP (protección contra fuerza bruta).
// Para las pruebas de carga con k6 se sube con la variable RateLimit__LoginPermitPerMinute.
var loginPermitPerMinute = builder.Configuration.GetValue("RateLimit:LoginPermitPerMinute", 10);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// CORS solo para el origen de la PWA.
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddPolicy("pwa", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("X-Cache", "Idempotent-Replayed", "Location")));

// ---------------------------------------------------------------------------
// Documentación OpenAPI (Swagger)
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FlashStore API",
        Version = "v1",
        Description = "API REST de FlashStore: catálogo, carrito, pedidos con control de concurrencia, pagos simulados y eventos asíncronos."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token JWT obtenido en POST /api/auth/login"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Detrás de Nginx (PWA): la IP real del cliente llega en X-Forwarded-For.
// Solo se confía en proxies de redes privadas (la red interna de Docker).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
});

// Errores no controlados → ProblemDetails sin exponer el stack trace.
builder.Services.AddProblemDetails();

var app = builder.Build();

if (inventoryOptions.Strategy == InventoryStrategy.Unsafe)
{
    app.Logger.LogWarning(
        "ATENCIÓN: Inventory:Strategy=Unsafe. Modo inseguro con sobreventa intencional (escenario NO-GO). No usar en producción.");
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Cabeceras de seguridad (reducen hallazgos de OWASP ZAP).
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        headers["Cache-Control"] = "no-store";
    }

    await next();
});

if (builder.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "FlashStore API");
}

app.UseCors("pwa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapProductEndpoints();
app.MapCartEndpoints();
app.MapOrderEndpoints();

app.Run();

// Permite usar WebApplicationFactory<Program> en las pruebas de integración.
public partial class Program
{
}
