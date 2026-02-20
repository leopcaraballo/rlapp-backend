namespace WaitingRoom.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string VirtualHost { get; init; } = "/";
    public string ExchangeName { get; init; } = "waitingroom.events";
    public string ExchangeType { get; init; } = "topic";
}
