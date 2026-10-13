using RabbitMQ.Client;

namespace FlashStore.Infrastructure.Messaging;

public sealed class RabbitMqSettings
{
    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string VirtualHost { get; init; } = "/";

    public string ClientName { get; init; } = "flashstore";
}

/// <summary>
/// Mantiene una sola conexión a RabbitMQ por proceso, con recuperación automática.
/// Si RabbitMQ está caído, GetConnection lanza excepción y quien llama decide reintentar.
/// </summary>
public sealed class RabbitMqConnectionProvider : IDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly object _lock = new();
    private IConnection? _connection;

    public RabbitMqConnectionProvider(RabbitMqSettings settings)
    {
        _factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            ClientProvidedName = settings.ClientName,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };
    }

    public bool IsConnected => _connection?.IsOpen == true;

    public IConnection GetConnection()
    {
        lock (_lock)
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = _factory.CreateConnection();
            return _connection;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}

/// <summary>
/// Topología de RabbitMQ:
///   flashstore.events (topic) → colas de negocio
///   flashstore.dlx   (direct) → colas .dlq (mensajes que fallaron 3 veces)
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "flashstore.events";
    public const string DeadLetterExchange = "flashstore.dlx";

    public const string OrdersQueue = "flashstore.orders";
    public const string InventoryQueue = "flashstore.inventory";
    public const string NotificationsQueue = "flashstore.notifications";

    public static readonly IReadOnlyDictionary<string, string[]> Bindings = new Dictionary<string, string[]>
    {
        [OrdersQueue] = ["payment.confirmed", "payment.rejected"],
        [InventoryQueue] = ["payment.rejected", "order.cancelled"],
        [NotificationsQueue] = ["#"]
    };

    public static string DeadLetterQueueFor(string queue) => $"{queue}.dlq";

    /// <summary>Declara exchanges, colas, DLQ y bindings. Es idempotente: puede llamarse muchas veces.</summary>
    public static void Declare(IModel channel)
    {
        channel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);

        foreach (var (queue, routingKeys) in Bindings)
        {
            var deadLetterQueue = DeadLetterQueueFor(queue);

            channel.QueueDeclare(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.QueueBind(deadLetterQueue, DeadLetterExchange, routingKey: queue);

            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queue
            });

            foreach (var routingKey in routingKeys)
            {
                channel.QueueBind(queue, Exchange, routingKey);
            }
        }
    }
}
