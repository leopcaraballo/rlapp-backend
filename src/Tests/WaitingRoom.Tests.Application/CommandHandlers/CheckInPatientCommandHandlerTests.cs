namespace WaitingRoom.Tests.Application.CommandHandlers;

using FluentAssertions;
using Moq;
using WaitingRoom.Application.CommandHandlers;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Exceptions;
using WaitingRoom.Domain.Events;
using BuildingBlocks.EventSourcing;
using WaitingRoom.Tests.Application.Fakes;

/// <summary>
/// Tests for CheckInPatientCommandHandler.
///
/// Testing strategy:
/// - Use mocks for infrastructure (IEventStore, IEventPublisher)
/// - Focus on orchestration logic
/// - Domain logic is tested in Domain tests
/// - Test both happy path and error scenarios
/// </summary>
public class CheckInPatientCommandHandlerTests
{
    /// <summary>
    /// Happy path test:
    /// Valid command → aggregate loads → patient checks in →
    /// events saved → events published → success
    /// </summary>
    [Fact]
    public async Task HandleAsync_ValidCommand_SavesAndPublishesEvents()
    {
        // ARRANGE
        var queueId = "QUEUE-01";
        var patientId = "PAT-001";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = patientId,
            PatientName = "John Doe",
            Priority = Priority.High,
            ConsultationType = "General",
            Notes = "Regular checkup",
            Actor = "nurse-001"
        };

        // Create a valid aggregate with existing event
        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Main Queue", 10, metadata);

        // Create mocks
        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        // Configure event store to return our queue
        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT
        var result = await handler.HandleAsync(command);

        // ASSERT
        // 1. Should return event count
        result.Should().BeGreaterThan(0);

        // 2. SaveAsync should be called once
        eventStoreMock.Verify(
            es => es.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // 3. PublishAsync should be called to publish events
        publisherMock.Verify(
            pub => pub.PublishAsync(It.IsAny<IEnumerable<DomainEvent>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Error scenario: Queue not found
    /// </summary>
    [Fact]
    public async Task HandleAsync_QueueNotFound_ThrowsAggregateNotFoundException()
    {
        // ARRANGE
        var queueId = "QUEUE-NOTFOUND";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = Priority.High,
            ConsultationType = "General",
            Actor = "nurse-001"
        };

        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        // Event store returns null (queue not found)
        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitingQueue?)null);

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT & ASSERT
        var exception = await Assert.ThrowsAsync<AggregateNotFoundException>(
            () => handler.HandleAsync(command));

        exception.AggregateId.Should().Be(queueId);
    }

    /// <summary>
    /// Domain validation scenario: Queue at capacity
    /// </summary>
    [Fact]
    public async Task HandleAsync_QueueAtCapacity_ThrowsDomainException()
    {
        // ARRANGE
        var queueId = "QUEUE-01";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = "PAT-999",
            PatientName = "Jane Doe",
            Priority = Priority.Low,
            ConsultationType = "General",
            Actor = "nurse-001"
        };

        // Create queue with capacity 1
        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Small Queue", maxCapacity: 1, metadata);

        // Add first patient to fill capacity
        queue.CheckInPatient(
            PatientId.Create("PAT-001"),
            "John Doe",
            Priority.Create(Priority.High),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            EventMetadata.CreateNew(queueId, "nurse-001"));

        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT & ASSERT
        // Domain should throw because queue is at capacity
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => handler.HandleAsync(command));

        exception.Message.Should().Contain("capacity");
    }

    /// <summary>
    /// Version conflict scenario: Concurrent modifications
    /// </summary>
    [Fact]
    public async Task HandleAsync_ConcurrentModification_ThrowsEventConflictException()
    {
        // ARRANGE
        var queueId = "QUEUE-01";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = Priority.High,
            ConsultationType = "General",
            Actor = "nurse-001"
        };

        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Main Queue", 10, metadata);

        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);

        // Simulate version conflict when saving
        eventStoreMock
            .Setup(es => es.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EventConflictException(queueId, expectedVersion: 1, actualVersion: 2));

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT & ASSERT
        var exception = await Assert.ThrowsAsync<EventConflictException>(
            () => handler.HandleAsync(command));

        exception.AggregateId.Should().Be(queueId);
    }

    /// <summary>
    /// Idempotency test: Commands themselves don't enforce idempotency.
    /// Idempotency is enforced by infrastructure using IdempotencyKey.
    /// This test validates that CorrelationId is preserved for infrastructure to use.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CommandPreservesIdempotencyKey_InfrastructureCanEnforceDedupplication()
    {
        // ARRANGE
        var queueId = "QUEUE-01";
        var idempotencyKey = "CMD-001-CHECKIN";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = Priority.High,
            ConsultationType = "General",
            Actor = "nurse-001",
            CorrelationId = idempotencyKey
        };

        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Main Queue", 10, metadata);

        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        WaitingQueue savedQueue = null!;
        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);

        eventStoreMock
            .Setup(es => es.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()))
            .Callback<WaitingQueue, CancellationToken>((q, _) => savedQueue = q);

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT
        var result = await handler.HandleAsync(command);

        // ASSERT
        // IdempotencyKey must be preserved in event metadata for infrastructure to detect duplicates
        result.Should().BeGreaterThan(0);
        savedQueue.Should().NotBeNull();

        // Events should have the idempotency key in the causation ID
        // so infrastructure can detect and deduplicate duplicate commands
        savedQueue.UncommittedEvents.Should().AllSatisfy(e =>
            e.Metadata.CorrelationId.Should().Be(idempotencyKey)
        );
    }

    /// <summary>
    /// Correlation ID test: Correlation ID should be preserved for tracing
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithCorrelationId_PreservesForTracing()
    {
        // ARRANGE
        var queueId = "QUEUE-01";
        var correlationId = "CORR-12345";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = Priority.High,
            ConsultationType = "General",
            Actor = "nurse-001",
            CorrelationId = correlationId
        };

        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Main Queue", 10, metadata);

        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        WaitingQueue savedAggregate = null!;
        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);

        eventStoreMock
            .Setup(es => es.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()))
            .Callback<WaitingQueue, CancellationToken>((agg, _) => savedAggregate = agg);

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT
        await handler.HandleAsync(command);

        // ASSERT
        // Verify correlation ID was passed through to events
        savedAggregate.Should().NotBeNull();
        savedAggregate.UncommittedEvents.Should().NotBeEmpty();
        savedAggregate.UncommittedEvents.First().Metadata.CorrelationId
            .Should().Be(correlationId);
    }

    /// <summary>
    /// Publishing test: After successful save, events must be published
    /// </summary>
    [Fact]
    public async Task HandleAsync_AfterSuccessfulSave_PublishesAllEvents()
    {
        // ARRANGE
        var queueId = "QUEUE-01";
        var command = new CheckInPatientCommand
        {
            QueueId = queueId,
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = Priority.High,
            ConsultationType = "General",
            Actor = "nurse-001"
        };

        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Main Queue", 10, metadata);

        var eventStoreMock = new Mock<IEventStore>();
        var publisherMock = new Mock<IEventPublisher>();

        eventStoreMock
            .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);

        IEnumerable<DomainEvent> publishedEvents = null!;
        publisherMock
            .Setup(pub => pub.PublishAsync(It.IsAny<IEnumerable<DomainEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DomainEvent>, CancellationToken>((events, _) => publishedEvents = events);

        var clock = new FakeClock();
        var handler = new CheckInPatientCommandHandler(eventStoreMock.Object, publisherMock.Object, clock);

        // ACT
        await handler.HandleAsync(command);

        // ASSERT
        publishedEvents.Should().NotBeNull();
        publishedEvents.Should().NotBeEmpty();
        publishedEvents.Should().AllSatisfy(e =>
            e.Should().BeOfType<PatientCheckedIn>());
    }
}
