namespace WaitingRoom.Tests.Domain.Aggregates;

using FluentAssertions;
using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Aggregates;
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
        return WaitingQueue.Create(queueId, queueName, maxCapacity, metadata);
    }

    [Fact]
    public void Create_WithValidData_CreatesQueue()
    {
        var queue = CreateQueue();

        queue.Id.Should().Be("QUEUE-01");
        queue.QueueName.Should().Be("Main Reception");
        queue.MaxCapacity.Should().Be(10);
        queue.CurrentCount.Should().Be(0);
        queue.Version.Should().Be(0);
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
        var patientId = PatientId.Create("PAT-001");
        var priority = Priority.Create(Priority.High);
        var consultationType = ConsultationType.Create("General");
        var metadata = EventMetadata.CreateNew(queue.Id, "nurse");

        queue.CheckInPatient(
            patientId: patientId,
            patientName: "John Doe",
            priority: priority,
            consultationType: consultationType,
            checkInTime: DateTime.UtcNow,
            metadata: metadata
        );

        queue.HasUncommittedEvents.Should().BeTrue();
        queue.UncommittedEvents.Should().HaveCount(1);
        queue.UncommittedEvents[0].Should().BeOfType<PatientCheckedIn>();
        queue.CurrentCount.Should().Be(1);
    }

    [Fact]
    public void CheckInPatient_AtCapacity_ThrowsDomainException()
    {
        var queue = CreateQueue(maxCapacity: 1);
        var metadata1 = EventMetadata.CreateNew(queue.Id, "nurse");

        queue.CheckInPatient(
            PatientId.Create("PAT-001"),
            "Patient 1",
            Priority.Create(Priority.Low),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            metadata1
        );

        var metadata2 = EventMetadata.CreateNew(queue.Id, "nurse");

        Assert.Throws<DomainException>(() =>
            queue.CheckInPatient(
                PatientId.Create("PAT-002"),
                "Patient 2",
                Priority.Create(Priority.Low),
                ConsultationType.Create("General"),
                DateTime.UtcNow,
                metadata2
            )
        );
    }

    [Fact]
    public void CheckInPatient_DuplicatePatient_ThrowsDomainException()
    {
        var queue = CreateQueue();
        var patientId = PatientId.Create("PAT-001");
        var priority = Priority.Create(Priority.Low);
        var consultationType = ConsultationType.Create("General");
        var metadata1 = EventMetadata.CreateNew(queue.Id, "nurse");

        queue.CheckInPatient(patientId, "John Doe", priority, consultationType, DateTime.UtcNow, metadata1);

        var metadata2 = EventMetadata.CreateNew(queue.Id, "nurse");

        Assert.Throws<DomainException>(() =>
            queue.CheckInPatient(patientId, "John Doe", priority, consultationType, DateTime.UtcNow, metadata2)
        );
    }

    [Fact]
    public void CheckInPatient_MultiplePatients_MaintainsOrder()
    {
        var queue = CreateQueue();
        var metadata1 = EventMetadata.CreateNew(queue.Id, "nurse");
        var metadata2 = EventMetadata.CreateNew(queue.Id, "nurse");

        queue.CheckInPatient(
            PatientId.Create("PAT-001"),
            "Patient 1",
            Priority.Create(Priority.Low),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            metadata1
        );

        queue.CheckInPatient(
            PatientId.Create("PAT-002"),
            "Patient 2",
            Priority.Create(Priority.Medium),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            metadata2
        );

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

        var metadata = EventMetadata.CreateNew(queue.Id, "nurse");
        queue.CheckInPatient(
            PatientId.Create("PAT-001"),
            "Patient 1",
            Priority.Create(Priority.Low),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            metadata
        );

        queue.CurrentCount.Should().Be(1);
        queue.AvailableCapacity.Should().Be(4);
        queue.IsAtCapacity.Should().BeFalse();
    }

    [Fact]
    public void ClearUncommittedEvents_AppliesToState()
    {
        var queue = CreateQueue();
        var metadata = EventMetadata.CreateNew(queue.Id, "nurse");
        queue.CheckInPatient(
            PatientId.Create("PAT-001"),
            "Patient 1",
            Priority.Create(Priority.Low),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            metadata
        );

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
        var patientId = PatientId.Create("PAT-001");
        var priority = Priority.Create(Priority.High);
        var consultationType = ConsultationType.Create("General");
        var metadata = EventMetadata.CreateNew("QUEUE-01", "nurse");

        queue1.CheckInPatient(patientId, "John Doe", priority, consultationType, now, metadata);
        queue2.CheckInPatient(patientId, "John Doe", priority, consultationType, now, metadata);

        queue1.CurrentCount.Should().Be(queue2.CurrentCount);
        queue1.Patients[0].PatientName.Should().Be(queue2.Patients[0].PatientName);
        queue1.Patients[0].QueuePosition.Should().Be(queue2.Patients[0].QueuePosition);
    }
}
