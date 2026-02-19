namespace WaitingRoom.Infrastructure.Messaging;

using System.Text;
using BuildingBlocks.EventSourcing;
using RabbitMQ.Client;
using WaitingRoom.Application.Ports;
using WaitingRoom.Infrastructure.Persistence.Outbox;
using WaitingRoom.Infrastructure.Serialization;

internal sealed class RabbitMqEventPublisher : IEventPublisher
{
    private readonly RabbitMqOptions _options;
    private readonly EventSerializer _serializer;
    private readonly PostgresOutboxStore? _outboxStore;

    public RabbitMqEventPublisher(
        RabbitMqOptions options,
        EventSerializer serializer,
        PostgresOutboxStore? outboxStore = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _outboxStore = outboxStore;
    }

    public Task PublishAsync(DomainEvent @event, CancellationToken cancellationToken = default) =>
        PublishAsync(new[] { @event }, cancellationToken);

    public async Task PublishAsync(
        IEnumerable<DomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events == null)
            throw new ArgumentNullException(nameof(events));

        var eventList = events.ToList();
        if (eventList.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        var eventIds = eventList.Select(e => Guid.Parse(e.Metadata.EventId)).ToList();

        try
        {
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.ExchangeDeclare(
                exchange: _options.ExchangeName,
                type: _options.ExchangeType,
                durable: true,
                autoDelete: false);

            foreach (var @event in eventList)
            {
                var payload = _serializer.Serialize(@event);
                var body = Encoding.UTF8.GetBytes(payload);
                var properties = channel.CreateBasicProperties();

                properties.MessageId = @event.Metadata.EventId;
                properties.CorrelationId = @event.Metadata.CorrelationId;
                properties.Type = @event.EventName;
                properties.ContentType = "application/json";
                properties.DeliveryMode = 2;

                var timestamp = new DateTimeOffset(@event.Metadata.OccurredAt).ToUnixTimeSeconds();
                properties.Timestamp = new AmqpTimestamp(timestamp);

                channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: @event.EventName,
                    basicProperties: properties,
                    body: body);
            }

            if (_outboxStore != null)
                await _outboxStore.MarkDispatchedAsync(eventIds, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_outboxStore != null)
            {
                await _outboxStore.MarkFailedAsync(
                    eventIds,
                    ex.Message,
                    retryAfter: TimeSpan.FromSeconds(30),
                    cancellationToken: cancellationToken);
            }

            throw;
        }
    }
}
