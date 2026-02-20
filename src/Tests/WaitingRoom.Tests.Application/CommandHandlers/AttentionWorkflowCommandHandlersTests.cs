namespace WaitingRoom.Tests.Application.CommandHandlers;

using BuildingBlocks.EventSourcing;
using FluentAssertions;
using Moq;
using WaitingRoom.Application.CommandHandlers;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Tests.Application.Fakes;
using Xunit;

public sealed class AttentionWorkflowCommandHandlersTests
{
    [Fact]
    public async Task CallNextCashierAndValidatePayment_ValidFlow_SavesEvents()
    {
        var queue = WaitingQueue.Create("QUEUE-1", "Main", 10, EventMetadata.CreateNew("QUEUE-1", "system"));
        queue.ClearUncommittedEvents();

        queue.CheckInPatient(new CheckInPatientRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            PatientName = "John",
            Priority = Priority.Create(Priority.High),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "reception")
        });

        var store = new Mock<IEventStore>();
        var publisher = new Mock<IEventPublisher>();
        var clock = new FakeClock();

        store.Setup(x => x.LoadAsync(queue.Id, It.IsAny<CancellationToken>())).ReturnsAsync(queue);

        var callHandler = new CallNextCashierCommandHandler(store.Object, publisher.Object, clock);
        var callResult = await callHandler.HandleAsync(new CallNextCashierCommand
        {
            QueueId = queue.Id,
            Actor = "cashier-1"
        });

        callResult.PatientId.Should().Be("PAT-001");

        var validateHandler = new ValidatePaymentCommandHandler(store.Object, publisher.Object, clock);
        var eventCount = await validateHandler.HandleAsync(new ValidatePaymentCommand
        {
            QueueId = queue.Id,
            PatientId = "PAT-001",
            Actor = "cashier-1"
        });

        eventCount.Should().BeGreaterThan(0);
        store.Verify(x => x.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        publisher.Verify(x => x.PublishAsync(It.IsAny<IEnumerable<DomainEvent>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ClaimNextPatient_ValidQueue_SavesAndPublishesEvents()
    {
        var queue = CreateQueueWithClaimablePatient();
        var store = new Mock<IEventStore>();
        var publisher = new Mock<IEventPublisher>();
        var clock = new FakeClock();

        store.Setup(x => x.LoadAsync(queue.Id, It.IsAny<CancellationToken>())).ReturnsAsync(queue);

        var handler = new ClaimNextPatientCommandHandler(store.Object, publisher.Object, clock);
        var result = await handler.HandleAsync(new ClaimNextPatientCommand
        {
            QueueId = queue.Id,
            Actor = "doctor-1",
            StationId = "S-01"
        });

        result.PatientId.Should().Be("PAT-001");
        store.Verify(x => x.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(x => x.PublishAsync(It.IsAny<IEnumerable<DomainEvent>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallAndCompleteAttention_ValidFlow_SavesEvents()
    {
        var queue = CreateQueueWithClaimedAndCalledPatient();
        var store = new Mock<IEventStore>();
        var publisher = new Mock<IEventPublisher>();
        var clock = new FakeClock();

        store.Setup(x => x.LoadAsync(queue.Id, It.IsAny<CancellationToken>())).ReturnsAsync(queue);

        var completeHandler = new CompleteAttentionCommandHandler(store.Object, publisher.Object, clock);

        var eventCount = await completeHandler.HandleAsync(new CompleteAttentionCommand
        {
            QueueId = queue.Id,
            PatientId = "PAT-001",
            Actor = "doctor-1",
            Outcome = "ok"
        });

        eventCount.Should().BeGreaterThan(0);
        store.Verify(x => x.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(x => x.PublishAsync(It.IsAny<IEnumerable<DomainEvent>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static WaitingQueue CreateQueueWithClaimablePatient()
    {
        var queue = WaitingQueue.Create("QUEUE-1", "Main", 10, EventMetadata.CreateNew("QUEUE-1", "system"));
        queue.ClearUncommittedEvents();

        queue.CheckInPatient(new CheckInPatientRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            PatientName = "John",
            Priority = Priority.Create(Priority.High),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "reception")
        });

        queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "cashier")
        });

        queue.ValidatePayment(new ValidatePaymentRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            ValidatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "cashier")
        });

        queue.ActivateConsultingRoom(new ActivateConsultingRoomRequest
        {
            ConsultingRoomId = "S-01",
            ActivatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "coordinator")
        });

        return queue;
    }

    private static WaitingQueue CreateQueueWithClaimedAndCalledPatient()
    {
        var queue = CreateQueueWithClaimablePatient();
        queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "doctor"),
            StationId = "S-01"
        });

        queue.CallPatient(new CallPatientRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "nurse")
        });

        return queue;
    }
}
