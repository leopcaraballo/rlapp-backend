namespace WaitingRoom.Tests.Domain.Aggregates;

using FluentAssertions;
using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Events;
using WaitingRoom.Domain.Exceptions;
using Xunit;

public class WaitingQueueTests
{
    private static WaitingQueue CreateQueue(
        string queueId = "QUEUE-01",
        string queueName = "Main Reception",
        int maxCapacity = 10)
    {
        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, queueName, maxCapacity, metadata);
        queue.ClearUncommittedEvents();
        return queue;
    }

    private static CheckInPatientRequest CreateCheckInRequest(
        string patientId = "PAT-001",
        string patientName = "John Doe",
        string priority = Priority.Low,
        string consultationType = "General",
        DateTime? checkInTime = null,
        string? notes = null)
    {
        var metadata = EventMetadata.CreateNew("QUEUE-01", "nurse");
        return new CheckInPatientRequest
        {
            PatientId = PatientId.Create(patientId),
            PatientName = patientName,
            Priority = Priority.Create(priority),
            ConsultationType = ConsultationType.Create(consultationType),
            CheckInTime = checkInTime ?? DateTime.UtcNow,
            Metadata = metadata,
            Notes = notes
        };
    }

    [Fact]
    public void Create_WithValidData_CreatesQueue()
    {
        var queue = CreateQueue();

        queue.Id.Should().Be("QUEUE-01");
        queue.QueueName.Should().Be("Main Reception");
        queue.MaxCapacity.Should().Be(10);
        queue.CurrentCount.Should().Be(0);
        queue.Version.Should().Be(1);
    }

    [Fact]
    public void Create_WithInvalidQueueName_ThrowsDomainException()
    {
        var metadata = EventMetadata.CreateNew("QUEUE-01", "system");
        Assert.Throws<DomainException>(() =>
            WaitingQueue.Create("QUEUE-01", "", 10, metadata));
    }

    [Fact]
    public void Create_WithZeroCapacity_ThrowsDomainException()
    {
        var metadata = EventMetadata.CreateNew("QUEUE-01", "system");
        Assert.Throws<DomainException>(() =>
            WaitingQueue.Create("QUEUE-01", "Main Reception", 0, metadata));
    }

    [Fact]
    public void CheckInPatient_WithValidData_EmitsPatientCheckedInEvent()
    {
        var queue = CreateQueue();
        var request = CreateCheckInRequest("PAT-001", "John Doe", Priority.High);

        queue.CheckInPatient(request);

        queue.HasUncommittedEvents.Should().BeTrue();
        queue.UncommittedEvents.Should().HaveCount(1);
        queue.UncommittedEvents[0].Should().BeOfType<PatientCheckedIn>();
        queue.CurrentCount.Should().Be(1);
    }

    [Fact]
    public void CheckInPatient_AtCapacity_ThrowsDomainException()
    {
        var queue = CreateQueue(maxCapacity: 1);
        var request1 = CreateCheckInRequest("PAT-001", "Patient 1", Priority.Low);

        queue.CheckInPatient(request1);

        var request2 = CreateCheckInRequest("PAT-002", "Patient 2", Priority.Low);

        Assert.Throws<DomainException>(() =>
            queue.CheckInPatient(request2)
        );
    }

    [Fact]
    public void CheckInPatient_DuplicatePatient_ThrowsDomainException()
    {
        var queue = CreateQueue();
        var request = CreateCheckInRequest("PAT-001", "John Doe", Priority.Low);

        queue.CheckInPatient(request);

        Assert.Throws<DomainException>(() =>
            queue.CheckInPatient(request)
        );
    }

    [Fact]
    public void CheckInPatient_MultiplePatients_MaintainsOrder()
    {
        var queue = CreateQueue();
        var request1 = CreateCheckInRequest("PAT-001", "Patient 1", Priority.Low);
        var request2 = CreateCheckInRequest("PAT-002", "Patient 2", Priority.Medium);

        queue.CheckInPatient(request1);
        queue.CheckInPatient(request2);

        queue.CurrentCount.Should().Be(2);
        queue.Patients[0].PatientId.Value.Should().Be("PAT-001");
        queue.Patients[1].PatientId.Value.Should().Be("PAT-002");
    }

    [Fact]
    public void Capacity_Properties_ReturnCorrectValues()
    {
        var queue = CreateQueue(maxCapacity: 5);

        queue.IsAtCapacity.Should().BeFalse();
        queue.AvailableCapacity.Should().Be(5);

        var request = CreateCheckInRequest("PAT-001", "Patient 1", Priority.Low);
        queue.CheckInPatient(request);

        queue.CurrentCount.Should().Be(1);
        queue.AvailableCapacity.Should().Be(4);
        queue.IsAtCapacity.Should().BeFalse();
    }

    [Fact]
    public void ClearUncommittedEvents_AppliesToState()
    {
        var queue = CreateQueue();
        var request = CreateCheckInRequest("PAT-001", "Patient 1", Priority.Low);
        queue.CheckInPatient(request);

        queue.ClearUncommittedEvents();

        queue.HasUncommittedEvents.Should().BeFalse();
        queue.UncommittedEvents.Should().HaveCount(0);
        queue.CurrentCount.Should().Be(1);
    }

    [Fact]
    public void Event_IsDeterministic_SameInputProducesSameResult()
    {
        var queue1 = CreateQueue("QUEUE-01");
        var queue2 = CreateQueue("QUEUE-01");

        var now = DateTime.UtcNow;
        var request1 = new CheckInPatientRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            PatientName = "John Doe",
            Priority = Priority.Create(Priority.High),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = now,
            Metadata = EventMetadata.CreateNew("QUEUE-01", "nurse")
        };

        var request2 = new CheckInPatientRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            PatientName = "John Doe",
            Priority = Priority.Create(Priority.High),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = now,
            Metadata = EventMetadata.CreateNew("QUEUE-01", "nurse")
        };

        queue1.CheckInPatient(request1);
        queue2.CheckInPatient(request2);

        queue1.CurrentCount.Should().Be(queue2.CurrentCount);
        queue1.Patients[0].PatientName.Should().Be(queue2.Patients[0].PatientName);
        queue1.Patients[0].QueuePosition.Should().Be(queue2.Patients[0].QueuePosition);
    }
}
