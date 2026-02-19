using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Events;
using WaitingRoom.Infrastructure.Messaging;
using WaitingRoom.Infrastructure.Persistence.Outbox;
using WaitingRoom.Infrastructure.Serialization;
using WaitingRoom.Worker;
using WaitingRoom.Worker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Configuration Options
        var outboxOptions = new OutboxDispatcherOptions();
        configuration.GetSection(OutboxDispatcherOptions.SectionName).Bind(outboxOptions);
        services.AddSingleton(outboxOptions);

        var rabbitMqOptions = new RabbitMqOptions();
        configuration.GetSection("RabbitMq").Bind(rabbitMqOptions);
        services.AddSingleton(rabbitMqOptions);

        var connectionString = configuration.GetConnectionString("EventStore")
            ?? throw new InvalidOperationException("EventStore connection string is required");

        // Infrastructure Services
        services.AddSingleton<IOutboxStore>(sp => new PostgresOutboxStore(connectionString));

        // Event Type Registry (register all domain events)
        services.AddSingleton(sp =>
        {
            // Use the default registry that includes all domain events
            return EventTypeRegistry.CreateDefault();
        });

        // Event Serializer
        services.AddSingleton<EventSerializer>();

        // Event Publisher (RabbitMQ)
        // IMPORTANT: OutboxStore is injected so publisher can mark messages as dispatched/failed
        services.AddSingleton<IEventPublisher>(sp =>
        {
            var options = sp.GetRequiredService<RabbitMqOptions>();
            var serializer = sp.GetRequiredService<EventSerializer>();
            var outboxStore = (PostgresOutboxStore)sp.GetRequiredService<IOutboxStore>();

            // Inject outboxStore so it can mark messages as dispatched/failed
            return new RabbitMqEventPublisher(options, serializer, outboxStore);
        });

        // Outbox Dispatcher
        services.AddSingleton<OutboxDispatcher>();

        // Background Worker
        services.AddHostedService<OutboxWorker>();
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    })
    .Build();

// Ensure database schema exists on startup
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Ensuring outbox database schema exists...");

var outboxStore = (PostgresOutboxStore)host.Services.GetRequiredService<IOutboxStore>();
await outboxStore.EnsureSchemaAsync();

logger.LogInformation("Database schema ready. Starting Outbox Worker...");

await host.RunAsync();

