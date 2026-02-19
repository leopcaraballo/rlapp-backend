namespace WaitingRoom.Tests.Projections.Idempotency;

using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WaitingRoom.Domain.Events;
using WaitingRoom.Infrastructure.Projections;
using WaitingRoom.Projections.Handlers;
using BuildingBlocks.EventSourcing;

/// <summary>
/// Tests for projection handler idempotency.
///
/// Requirement: Same event processed twice MUST produce identical state.
/// This is critical for:
/// - Handling network retries
/// - Rebuild from event store
/// - Deduplication on restart
/// </summary>
public sealed class PatientCheckedInIdempotencyTests
{
    private readonly PatientCheckedInProjectionHandler _handler;
    private readonly InMemoryWaitingRoomProjectionContext _context;

    public PatientCheckedInIdempotencyTests()
    {
        _handler = new PatientCheckedInProjectionHandler();
        _context = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
    }

    [Fact]
    public async Task handler_is_idempotent_same_event_twice_produces_same_state()
    {
        // Arrange
        var evt = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-123",
            patientName: "John Doe",
            priority: "high");

        // Act - Process first time
        await _handler.HandleAsync(evt, _context);
        var state1 = await _context.GetMonitorViewAsync("queue-1");
        var queueState1 = await _context.GetQueueStateViewAsync("queue-1");

        // Act - Process second time (same event)
        await _handler.HandleAsync(evt, _context);
        var state2 = await _context.GetMonitorViewAsync("queue-1");
        var queueState2 = await _context.GetQueueStateViewAsync("queue-1");

        // Assert - States must be identical
        state1.Should().NotBeNull();
        state2.Should().NotBeNull();
        state1!.TotalPatientsWaiting.Should().Be(state2!.TotalPatientsWaiting);
        state1.HighPriorityCount.Should().Be(state2.HighPriorityCount);

        queueState1.Should().NotBeNull();
        queueState2.Should().NotBeNull();
        queueState1!.CurrentCount.Should().Be(queueState2!.CurrentCount);
        queueState1.PatientsInQueue.Count.Should().Be(queueState2.PatientsInQueue.Count);
    }

    [Fact]
    public async Task handler_processes_high_priority_correctly()
    {
        // Arrange
        var evt = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-high",
            patientName: "High Priority Patient",
            priority: "high");

        // Act
        await _handler.HandleAsync(evt, _context);
        var monitor = await _context.GetMonitorViewAsync("queue-1");

        // Assert
        monitor.Should().NotBeNull();
        monitor!.HighPriorityCount.Should().Be(1);
        monitor.NormalPriorityCount.Should().Be(0);
        monitor.LowPriorityCount.Should().Be(0);
        monitor.TotalPatientsWaiting.Should().Be(1);
    }

    [Fact]
    public async Task handler_processes_normal_priority_correctly()
    {
        // Arrange
        var evt = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-normal",
            patientName: "Normal Priority Patient",
            priority: "normal");

        // Act
        await _handler.HandleAsync(evt, _context);
        var monitor = await _context.GetMonitorViewAsync("queue-1");

        // Assert
        monitor.Should().NotBeNull();
        monitor!.HighPriorityCount.Should().Be(0);
        monitor.NormalPriorityCount.Should().Be(1);
        monitor.LowPriorityCount.Should().Be(0);
    }

    [Fact]
    public async Task handler_adds_patient_to_queue_state()
    {
        // Arrange
        var evt = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-001",
            patientName: "Patient One",
            priority: "normal");

        // Act
        await _handler.HandleAsync(evt, _context);
        var queueState = await _context.GetQueueStateViewAsync("queue-1");

        // Assert
        queueState.Should().NotBeNull();
        queueState!.CurrentCount.Should().Be(1);
        queueState.PatientsInQueue.Should().HaveCount(1);
        queueState.PatientsInQueue[0].PatientId.Should().Be("patient-001");
        queueState.PatientsInQueue[0].PatientName.Should().Be("Patient One");
        queueState.PatientsInQueue[0].Priority.Should().Be("normal");
    }

    [Fact]
    public async Task handler_maintains_priority_order_in_queue()
    {
        // Arrange - Add patients in order: normal, low, high
        var normal = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-1",
            patientName: "Normal Patient",
            priority: "normal");

        var low = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-2",
            patientName: "Low Patient",
            priority: "low");

        var high = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-3",
            patientName: "High Patient",
            priority: "high");

        // Act
        await _handler.HandleAsync(normal, _context);
        await _handler.HandleAsync(low, _context);
        await _handler.HandleAsync(high, _context);

        var queueState = await _context.GetQueueStateViewAsync("queue-1");

        // Assert - Should be sorted: high, normal, low
        queueState!.PatientsInQueue.Should().HaveCount(3);
        queueState.PatientsInQueue[0].Priority.Should().Be("high");
        queueState.PatientsInQueue[1].Priority.Should().Be("normal");
        queueState.PatientsInQueue[2].Priority.Should().Be("low");
    }

    [Fact]
    public async Task handler_duplicate_event_does_not_add_patient_twice()
    {
        // Arrange
        var evt = CreatePatientCheckedInEvent(
            queueId: "queue-1",
            patientId: "patient-123",
            patientName: "Same Patient",
            priority: "high");

        // Act - Process same event 3 times
        await _handler.HandleAsync(evt, _context);
        await _handler.HandleAsync(evt, _context);
        await _handler.HandleAsync(evt, _context);

        var queueState = await _context.GetQueueStateViewAsync("queue-1");

        // Assert - Only 1 patient in queue
        queueState!.CurrentCount.Should().Be(1);
        queueState.PatientsInQueue.Should().HaveCount(1);
        queueState.PatientsInQueue[0].PatientId.Should().Be("patient-123");
    }

    private static PatientCheckedIn CreatePatientCheckedInEvent(
        string queueId,
        string patientId,
        string patientName,
        string priority)
    {
        return new PatientCheckedIn
        {
            QueueId = queueId,
            PatientId = patientId,
            PatientName = patientName,
            Priority = priority,
            Metadata = new EventMetadata
            {
                AggregateId = queueId,
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString(),
                CausationId = Guid.NewGuid().ToString(),
                Actor = "system",
                OccurredAt = DateTimeOffset.UtcNow,
                Version = 1,
                Timestamp = DateTime.UtcNow
            }
        };
    }
}
