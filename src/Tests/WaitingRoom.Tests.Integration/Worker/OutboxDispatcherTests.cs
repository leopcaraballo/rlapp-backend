namespace WaitingRoom.Tests.Integration.Worker;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WaitingRoom.Application.Ports;
using WaitingRoom.Infrastructure.Serialization;
using WaitingRoom.Worker;
using WaitingRoom.Worker.Services;
using Xunit;

/// <summary>
/// Integration tests for OutboxDispatcher.
///
/// Test Strategy:
/// - Use fake/mock implementations for dependencies
/// - Test integration between Dispatcher, Store, Publisher
/// - Validate idempotency, retry logic, failure handling
/// - NO real infrastructure (PostgreSQL, RabbitMQ)
/// - Deterministic, fast, isolated
/// </summary>
public sealed class OutboxDispatcherTests
{
    private readonly Mock<IEventPublisher> _publisherMock;
    private readonly FakeOutboxStore _outboxStore;
    private readonly EventSerializer _serializer;
    private readonly OutboxDispatcherOptions _options;
    private readonly OutboxDispatcher _dispatcher;

    public OutboxDispatcherTests()
    {
        _publisherMock = new Mock<IEventPublisher>();
        _outboxStore = new FakeOutboxStore();

        // Register TestDomainEvent for serialization
        var registry = new EventTypeRegistry(new[] { typeof(TestDomainEvent) });
        _serializer = new EventSerializer(registry);

        _options = new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxRetryAttempts = 3,
            BaseRetryDelaySeconds = 1,
            MaxRetryDelaySeconds = 10
        };

        _dispatcher = new OutboxDispatcher(
            _outboxStore,
            _publisherMock.Object,
            _serializer,
            _options,
            NullLogger<OutboxDispatcher>.Instance);
    }

    [Fact]
    public async Task DispatchBatchAsync_NoPendingMessages_ReturnsZero()
    {
        // Arrange
        // Store is empty by default

        // Act
        var result = await _dispatcher.DispatchBatchAsync();

        // Assert
        result.Should().Be(0);
        _publisherMock.Verify(
            p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchBatchAsync_PendingMessages_PublishesAndMarksDispatched()
    {
        // Arrange
        var testEvent = CreateTestEvent();
        var payload = _serializer.Serialize(testEvent);
        var message = OutboxMessage.FromEvent(testEvent, payload);

        _outboxStore.AddPendingMessage(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _dispatcher.DispatchBatchAsync();

        // Assert
        result.Should().Be(1);

        _publisherMock.Verify(
            p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _outboxStore.DispatchedEventIds.Should().Contain(message.EventId);
    }

    [Fact]
    public async Task DispatchBatchAsync_PublishFails_MarksAsFailed()
    {
        // Arrange
        var testEvent = CreateTestEvent();
        var payload = _serializer.Serialize(testEvent);
        var message = OutboxMessage.FromEvent(testEvent, payload);

        _outboxStore.AddPendingMessage(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ connection failed"));

        // Act
        var result = await _dispatcher.DispatchBatchAsync();

        // Assert
        result.Should().Be(0); // No successful dispatches

        _outboxStore.FailedEventIds.Should().Contain(message.EventId);
        _outboxStore.LastFailureError.Should().Contain("RabbitMQ connection failed");
    }

    [Fact]
    public async Task DispatchBatchAsync_ExceedsMaxRetries_MarksPermanentlyFailed()
    {
        // Arrange
        var testEvent = CreateTestEvent();
        var payload = _serializer.Serialize(testEvent);
        var message = OutboxMessage.FromEvent(testEvent, payload) with
        {
            Attempts = _options.MaxRetryAttempts - 1 // One more attempt will exceed max
        };

        _outboxStore.AddPendingMessage(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Permanent failure"));

        // Act
        var result = await _dispatcher.DispatchBatchAsync();

        // Assert
        result.Should().Be(0);

        _outboxStore.FailedEventIds.Should().Contain(message.EventId);
        _outboxStore.LastRetryDelay.Should().BeCloseTo(TimeSpan.FromDays(365), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task DispatchBatchAsync_MultipleMessages_ProcessesAll()
    {
        // Arrange
        var messages = new List<OutboxMessage>();
        for (int i = 0; i < 5; i++)
        {
            var testEvent = CreateTestEvent();
            var payload = _serializer.Serialize(testEvent);
            var message = OutboxMessage.FromEvent(testEvent, payload);
            messages.Add(message);
            _outboxStore.AddPendingMessage(message);
        }

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _dispatcher.DispatchBatchAsync();

        // Assert
        result.Should().Be(5);

        _publisherMock.Verify(
            p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));

        foreach (var message in messages)
        {
            _outboxStore.DispatchedEventIds.Should().Contain(message.EventId);
        }
    }

    [Fact]
    public async Task DispatchBatchAsync_PartialFailure_DispatchesSuccessfulOnes()
    {
        // Arrange
        var successEvent = CreateTestEvent();
        var successPayload = _serializer.Serialize(successEvent);
        var successMessage = OutboxMessage.FromEvent(successEvent, successPayload);
        _outboxStore.AddPendingMessage(successMessage);

        var failEvent = CreateTestEvent();
        var failPayload = _serializer.Serialize(failEvent);
        var failMessage = OutboxMessage.FromEvent(failEvent, failPayload);
        _outboxStore.AddPendingMessage(failMessage);

        var callCount = 0;
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 2) // Second call fails
                    throw new InvalidOperationException("Second message failed");
                return Task.CompletedTask;
            });

        // Act
        var result = await _dispatcher.DispatchBatchAsync();

        // Assert
        result.Should().Be(1); // Only one success

        _outboxStore.DispatchedEventIds.Should().Contain(successMessage.EventId);
        _outboxStore.FailedEventIds.Should().Contain(failMessage.EventId);
    }

    private static TestDomainEvent CreateTestEvent()
    {
        var metadata = new EventMetadata
        {
            EventId = Guid.NewGuid().ToString(),
            AggregateId = Guid.NewGuid().ToString(),
            Version = 1,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString(),
            CausationId = Guid.NewGuid().ToString(),
            IdempotencyKey = Guid.NewGuid().ToString(),
            Actor = "test-actor",
            SchemaVersion = 1
        };

        return new TestDomainEvent { Metadata = metadata };
    }
}

/// <summary>
/// Fake in-memory outbox store for testing.
/// Simulates PostgresOutboxStore behavior without database.
/// </summary>
internal sealed class FakeOutboxStore : IOutboxStore
{
    private readonly List<OutboxMessage> _pendingMessages = new();

    public List<Guid> DispatchedEventIds { get; } = new();
    public List<Guid> FailedEventIds { get; } = new();
    public string? LastFailureError { get; private set; }
    public TimeSpan? LastRetryDelay { get; private set; }

    public void AddPendingMessage(OutboxMessage message)
    {
        _pendingMessages.Add(message);
    }

    public Task AddAsync(
        List<OutboxMessage> messages,
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        // Fake implementation: just add to pending
        _pendingMessages.AddRange(messages);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var pending = _pendingMessages.Take(batchSize).ToList();
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    public Task MarkDispatchedAsync(
        IEnumerable<Guid> eventIds,
        CancellationToken cancellationToken = default)
    {
        DispatchedEventIds.AddRange(eventIds);
        _pendingMessages.RemoveAll(m => eventIds.Contains(m.EventId));
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        IEnumerable<Guid> eventIds,
        string error,
        TimeSpan retryAfter,
        CancellationToken cancellationToken = default)
    {
        FailedEventIds.AddRange(eventIds);
        LastFailureError = error;
        LastRetryDelay = retryAfter;
        _pendingMessages.RemoveAll(m => eventIds.Contains(m.EventId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Simple test domain event for testing serialization/deserialization.
/// </summary>
internal sealed record TestDomainEvent : DomainEvent
{
    public string TestData { get; init; } = "test-data";
}
