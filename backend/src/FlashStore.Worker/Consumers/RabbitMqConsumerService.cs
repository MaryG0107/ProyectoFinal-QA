using System.Text;
using FlashStore.Infrastructure.Messaging;
using FlashStore.Worker.Handlers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FlashStore.Worker.Consumers;

public sealed class WorkerOptions
{
    /// <summary>
    /// Cola en la que se simula una falla (para demostrar reintentos y DLQ en la defensa).
    /// Acepta el nombre corto ("notifications") o el completo ("flashstore.notifications").
    /// Vacío = sin fallas simuladas.
    /// </summary>
    public string? SimulateFailureQueue { get; init; }

    public static WorkerOptions FromConfiguration(IConfiguration configuration)
    {
        var raw = configuration["Worker:SimulateFailureQueue"]?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            return new WorkerOptions();
        }

        return new WorkerOptions
        {
            SimulateFailureQueue = raw.StartsWith("flashstore.", StringComparison.OrdinalIgnoreCase) ? raw : $"flashstore.{raw}"
        };
    }
}

/// <summary>
/// Conecta cada IMessageHandler con su cola.
/// - Confirmación manual (ack) solo cuando el mensaje se procesó bien.
/// - Si falla: hasta 3 reintentos con espera de 1, 2 y 4 segundos.
/// - Después del tercer reintento el mensaje se rechaza y RabbitMQ lo envía a la DLQ.
/// </summary>
public sealed class RabbitMqConsumerService : BackgroundService
{
    private const int MaxRetries = 3;
    private const string RetryHeader = "x-retry-count";

    private readonly RabbitMqConnectionProvider _rabbit;
    private readonly IReadOnlyList<IMessageHandler> _handlers;
    private readonly WorkerOptions _options;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly List<IModel> _channels = [];

    public RabbitMqConsumerService(
        RabbitMqConnectionProvider rabbit,
        IEnumerable<IMessageHandler> handlers,
        WorkerOptions options,
        ILogger<RabbitMqConsumerService> logger)
    {
        _rabbit = rabbit;
        _handlers = handlers.ToList();
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.IsNullOrEmpty(_options.SimulateFailureQueue))
        {
            _logger.LogWarning("FALLA SIMULADA ACTIVA en la cola {Queue}: sus mensajes terminarán en la DLQ", _options.SimulateFailureQueue);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartConsumers(stoppingToken);
                _logger.LogInformation("Worker conectado a RabbitMQ con {Count} consumidores", _handlers.Count);
                break;
            }
            catch (Exception ex)
            {
                CloseChannels();
                _logger.LogWarning(ex, "RabbitMQ no disponible. Reintento en 5 segundos");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Los consumidores trabajan por eventos; la recuperación automática del cliente
        // de RabbitMQ los reconecta si el broker se reinicia.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        CloseChannels();
    }

    private void StartConsumers(CancellationToken stoppingToken)
    {
        var connection = _rabbit.GetConnection();

        using (var setupChannel = connection.CreateModel())
        {
            RabbitMqTopology.Declare(setupChannel);
        }

        foreach (var handler in _handlers)
        {
            var channel = connection.CreateModel();
            channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += (_, delivery) => OnMessageAsync(channel, handler, delivery, stoppingToken);

            channel.BasicConsume(handler.Queue, autoAck: false, consumer: consumer);
            _channels.Add(channel);

            _logger.LogInformation("Consumidor {Consumer} escuchando la cola {Queue}", handler.ConsumerName, handler.Queue);
        }
    }

    private async Task OnMessageAsync(IModel channel, IMessageHandler handler, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var properties = delivery.BasicProperties;
        var body = delivery.Body.ToArray();

        if (!Guid.TryParse(properties?.MessageId, out var messageId))
        {
            _logger.LogError("Mensaje sin MessageId válido en {Queue}; se envía a la DLQ", handler.Queue);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
            return;
        }

        var message = new IncomingMessage(messageId, properties!.Type ?? string.Empty, Encoding.UTF8.GetString(body));

        try
        {
            if (string.Equals(_options.SimulateFailureQueue, handler.Queue, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Falla simulada en el consumidor (Worker__SimulateFailureQueue).");
            }

            await handler.HandleAsync(message, stoppingToken);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            var retryCount = GetRetryCount(properties);

            if (retryCount < MaxRetries)
            {
                var nextAttempt = retryCount + 1;
                _logger.LogWarning(ex, "Error procesando {Type} {MessageId} en {Queue}. Reintento {Attempt}/{Max}",
                    message.Type, messageId, handler.Queue, nextAttempt, MaxRetries);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), CancellationToken.None);

                var retryProperties = channel.CreateBasicProperties();
                retryProperties.Persistent = true;
                retryProperties.MessageId = properties.MessageId;
                retryProperties.Type = properties.Type;
                retryProperties.ContentType = "application/json";
                retryProperties.Headers = new Dictionary<string, object>
                {
                    [RetryHeader] = nextAttempt,
                    ["x-original-routing-key"] = delivery.RoutingKey,
                    ["x-last-error"] = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message
                };

                // Se vuelve a publicar en la misma cola (exchange por defecto) y se confirma el original.
                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: handler.Queue,
                    mandatory: false,
                    basicProperties: retryProperties,
                    body: body);

                channel.BasicAck(delivery.DeliveryTag, multiple: false);
            }
            else
            {
                _logger.LogError(ex, "Mensaje {MessageId} ({Type}) falló {Max} reintentos. Enviado a {Dlq}",
                    messageId, message.Type, MaxRetries, RabbitMqTopology.DeadLetterQueueFor(handler.Queue));

                channel.BasicReject(delivery.DeliveryTag, requeue: false);
            }
        }
    }

    private static int GetRetryCount(IBasicProperties? properties)
    {
        if (properties?.Headers is null || !properties.Headers.TryGetValue(RetryHeader, out var value))
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            short s => s,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    private void CloseChannels()
    {
        foreach (var channel in _channels)
        {
            try
            {
                if (channel.IsOpen)
                {
                    channel.Close();
                }

                channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error cerrando canal de RabbitMQ");
            }
        }

        _channels.Clear();
    }
}
