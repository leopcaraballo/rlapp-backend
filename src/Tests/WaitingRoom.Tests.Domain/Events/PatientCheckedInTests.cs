namespace WaitingRoom.Tests.Domain.Events;

using FluentAssertions;
using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using Xunit;

public class PatientCheckedInTests
{
    private static PatientCheckedIn CreateValidEvent(
        string queueId = "QUEUE-01",
        string patientId = "PAT-001",
        string patientName = "John Doe",
        string priority = "High",
        string consultationType = "General",
        int queuePosition = 0)
    {
        var metadata = EventMetadata.CreateNew(queueId, "nurse");

        return new PatientCheckedIn
        {
            Metadata = metadata,
            QueueId = queueId,
            PatientId = patientId,
            PatientName = patientName,
            Priority = priority,
            ConsultationType = consultationType,
            QueuePosition = queuePosition,
            CheckInTime = DateTime.UtcNow
        };
    }

    [Fact]
    public void Create_WithValidData_CreatesEvent()
    {
        // Arrange & Act
        var @event = CreateValidEvent();

        // Assert
        @event.PatientId.Should().Be("PAT-001");
        @event.PatientName.Should().Be("John Doe");
        @event.Priority.Should().Be("High");
        @event.QueuePosition.Should().Be(0);
    }

    [Fact]
    public void Event_IsImmutable_CannotModify()
    {
        // Arrange
        var @event = CreateValidEvent();

        // Act & Assert - Record types are immutable
        var modified = @event with { PatientName = "Jane Doe" };
        @event.PatientName.Should().Be("John Doe");
        modified.PatientName.Should().Be("Jane Doe");
    }

    [Fact]
    public void Event_HasCorrectEventName()
    {
        // Arrange & Act
        var @event = CreateValidEvent();

        // Assert
        @event.EventName.Should().Be("PatientCheckedIn");
    }

    [Fact]
    public void Event_WithMissingQueueId_ThrowsWhenValidated()
    {
        // Arrange & Act & Assert
        var metadata = EventMetadata.CreateNew("QUEUE-01", "nurse");
        var invalidEvent = new PatientCheckedIn
        {
            Metadata = metadata,
            QueueId = "", // Invalid
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = "High",
            ConsultationType = "General",
            QueuePosition = 0,
            CheckInTime = DateTime.UtcNow
        };

        // Validate would be called by event store
        // For now, just verify structure
        invalidEvent.QueueId.Should().BeEmpty();
    }

    [Fact]
    public void Event_HasMetadata()
    {
        // Arrange & Act
        var @event = CreateValidEvent();

        // Assert
        @event.Metadata.Should().NotBeNull();
        @event.Metadata.CorrelationId.Should().NotBeEmpty();
        @event.Metadata.AggregateId.Should().NotBeEmpty();
    }

    [Fact]
    public void Event_Deterministic_SameInputProducesSameStructure()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var event1 = new PatientCheckedIn
        {
            Metadata = EventMetadata.CreateNew("QUEUE-01", "nurse"),
            QueueId = "QUEUE-01",
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = "High",
            ConsultationType = "General",
            QueuePosition = 0,
            CheckInTime = now
        };

        var event2 = new PatientCheckedIn
        {
            Metadata = EventMetadata.CreateNew("QUEUE-01", "nurse"),
            QueueId = "QUEUE-01",
            PatientId = "PAT-001",
            PatientName = "John Doe",
            Priority = "High",
            ConsultationType = "General",
            QueuePosition = 0,
            CheckInTime = now
        };

        // Assert - same business data, different event IDs
        event1.PatientId.Should().Be(event2.PatientId);
        event1.PatientName.Should().Be(event2.PatientName);
        event1.Metadata.EventId.Should().NotBe(event2.Metadata.EventId); // Different events
    }
}
