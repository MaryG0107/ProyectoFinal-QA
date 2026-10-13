using FlashStore.Infrastructure;
using FlashStore.Worker.Consumers;
using FlashStore.Worker.Handlers;

var builder = Host.CreateApplicationBuilder(args);

// PostgreSQL, Redis, RabbitMQ y despachador del outbox (publica InventoryUpdated).
builder.Services.AddFlashStoreInfrastructure(builder.Configuration, "flashstore-worker");

builder.Services.AddSingleton(WorkerOptions.FromConfiguration(builder.Configuration));

// Un consumidor por cola de negocio.
builder.Services.AddSingleton<IMessageHandler, OrderStatusHandler>();
builder.Services.AddSingleton<IMessageHandler, InventoryRestockHandler>();
builder.Services.AddSingleton<IMessageHandler, NotificationHandler>();

builder.Services.AddHostedService<RabbitMqConsumerService>();

builder.Build().Run();
