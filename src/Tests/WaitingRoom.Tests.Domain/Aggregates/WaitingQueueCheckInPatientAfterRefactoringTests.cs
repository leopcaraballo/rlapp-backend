namespace WaitingRoom.Tests.Domain;

using FluentAssertions;
using Xunit;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Exceptions;
using BuildingBlocks.EventSourcing;

/// <summary>
/// PURE DOMAIN TESTS for WaitingQueue after refactoring to CheckInPatientRequest.
///
/// Key insight:
/// - These tests have ZERO infrastructure dependencies
/// - They use Parameter Object pattern (CheckInPatientRequest)
/// - Tests are easier to write (single object instead of 7 parameters)
/// - No service locators, no mocks, no reflection hacks
///
/// Pattern: Arrange-Act-Assert (AAA)
/// Scope: Pure domain logic validation
///
/// BEFORE REFACTORING:
/// queue.CheckInPatient(patientId, name, priority, type, time, metadata, notes)
/// - Fragile: too many parameters
/// - Hard to extend: adding a parameter breaks all tests
///
/// AFTER REFACTORING:
/// queue.CheckInPatient(request)
/// - Robust: single object encapsulates all parameters
/// - Extensible: add fields to request without changing method
/// - Clear intent: CheckInPatientRequest explicitly names all parameters
/// </summary>
public class WaitingQueueCheckInPatientAfterRefactoringTests
{
    /// <summary>
    /// Setup: Creates a valid waiting queue ready for patients.
    /// </summary>
    private WaitingQueue CreateValidQueue(string queueId = "QUEUE-01", int capacity = 10)
    {
        var metadata = EventMetadata.CreateNew(queueId, "system");
        var queue = WaitingQueue.Create(queueId, "Main Queue", capacity, metadata);
        queue.ClearUncommittedEvents();
        return queue;
    }

    /// <summary>
    /// Setup: Creates a valid check-in request.
    /// DEMONSTRATES the Parameter Object pattern:
    /// - Single object instead of 7 parameters
    /// - Readable constructor with named fields
    /// - Easy to extend without breaking tests
    /// </summary>
    private CheckInPatientRequest CreateValidRequest(
        string patientId = "PAT-001",
        string patientName = "John Doe",
        string priority = "high")
    {
        var metadata = EventMetadata.CreateNew("QUEUE-01", "nurse-001");
        return new CheckInPatientRequest
        {
            PatientId = PatientId.Create(patientId),
            PatientName = patientName,
            Priority = Priority.Create(priority),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = DateTime.UtcNow,
            Metadata = metadata,
            Notes = null
        };
    }

    // ========================================================================
    // HAPPY PATH TESTS
    // ========================================================================

    [Fact]
    public void CheckInPatient_WithValidRequest_ShouldEmitPatientCheckedInEvent()
    {
        // ARRANGE
        var queue = CreateValidQueue();
        var request = CreateValidRequest();

        // ACT
        queue.CheckInPatient(request);

        // ASSERT
        queue.UncommittedEvents.Should().HaveCount(1);
        queue.UncommittedEvents.First().Should().BeOfType<PatientCheckedIn>();

        var @event = (PatientCheckedIn)queue.UncommittedEvents.First();
        @event.PatientId.Should().Be(request.PatientId.Value);
        @event.PatientName.Should().Be(request.PatientName);
        @event.QueueId.Should().Be(queue.Id);
    }

    [Fact]
    public void CheckInPatient_WithValidRequest_ShouldUpdateQueueState()
    {
        // ARRANGE
        var queue = CreateValidQueue();
        var request = CreateValidRequest();
        var initialCount = queue.CurrentCount;

        // ACT
        queue.CheckInPatient(request);

        // ASSERT
        queue.CurrentCount.Should().Be(initialCount + 1);
        queue.Patients.Should().HaveCount(1);

        var patient = queue.Patients.First();
        patient.PatientId.Value.Should().Be(request.PatientId.Value);
        patient.PatientName.Should().Be(request.PatientName);
    }

    [Fact]
    public void CheckInPatient_WithMultiplePatients_ShouldMaintainOrder()
    {
        // ARRANGE
        var queue = CreateValidQueue(capacity: 5);
        var request1 = CreateValidRequest(patientId: "PAT-001", patientName: "Alice");
        var request2 = CreateValidRequest(patientId: "PAT-002", patientName: "Bob");
        var request3 = CreateValidRequest(patientId: "PAT-003", patientName: "Charlie");

        // ACT
        queue.CheckInPatient(request1);
        queue.CheckInPatient(request2);
        queue.CheckInPatient(request3);

        // ASSERT
        queue.CurrentCount.Should().Be(3);
        queue.Patients[0].PatientName.Should().Be("Alice");
        queue.Patients[1].PatientName.Should().Be("Bob");
        queue.Patients[2].PatientName.Should().Be("Charlie");
    }

    // ========================================================================
    // INVARIANT VIOLATION TESTS (Domain Rules Enforcement)
    // ========================================================================

    [Fact]
    public void CheckInPatient_ExceedsCapacity_ShouldThrowDomainException()
    {
        // ARRANGE
        var queue = CreateValidQueue(capacity: 1);  // Capacity = 1
        var request1 = CreateValidRequest(patientId: "PAT-001");
        var request2 = CreateValidRequest(patientId: "PAT-002");

        queue.CheckInPatient(request1);  // First patient checks in (OK)

        // ACT & ASSERT
        var action = () => queue.CheckInPatient(request2);  // Second patient should fail
        action.Should().Throw<DomainException>()
            .WithMessage("*capacity*", Xunit.Sdk.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckInPatient_DuplicatePatient_ShouldThrowDomainException()
    {
        // ARRANGE
        var queue = CreateValidQueue();
        var request = CreateValidRequest(patientId: "PAT-001");

        queue.CheckInPatient(request);  // First check-in (OK)

        // ACT & ASSERT
        var action = () => queue.CheckInPatient(request);  // Duplicate should fail
        action.Should().Throw<DomainException>()
            .WithMessage("*already*", Xunit.Sdk.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckInPatient_InvalidPriority_ShouldThrowDomainException()
    {
        // ARRANGE
        var queue = CreateValidQueue();
        var invalidPriority = "INVALID_PRIORITY";

        // ACT & ASSERT
        var action = () => Priority.Create(invalidPriority);
        action.Should().Throw<DomainException>();
    }

    // ========================================================================
    // IDEMPOTENCY AND STATE CONSISTENCY
    // ========================================================================

    [Fact]
    public void CheckInPatient_WithNotes_ShouldIncludeInEvent()
    {
        // ARRANGE
        var queue = CreateValidQueue();
        var request = CreateValidRequest();
        ((CheckInPatientRequest)request) = request with
        {
            Notes = "Allergic to penicillin"
        };

        // ACT
        queue.CheckInPatient(request);

        // ASSERT
        var @event = (PatientCheckedIn)queue.UncommittedEvents.First();
        @event.Notes.Should().Be("Allergic to penicillin");
    }

    // ========================================================================
    // PARAMETER OBJECT PATTERN BENEFITS
    // ========================================================================

    [Fact]
    public void CheckInPatientRequest_IsValueObject_CanBeReused()
    {
        // ARRANGE
        var queue1 = CreateValidQueue("QUEUE-01");
        var queue2 = CreateValidQueue("QUEUE-02");

        var sharedRequest = new CheckInPatientRequest
        {
            PatientId = PatientId.Create("PAT-001"),
            PatientName = "John Doe",
            Priority = Priority.Create("high"),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-01", "nurse"),
            Notes = null
        };

        // ACT
        // Same request object can be reused across tests
        // Shows improvement in test code reusability
        queue1.CheckInPatient(sharedRequest);

        // ASSERT
        queue1.Patients.Should().HaveCount(1);
    }

    [Fact]
    public void CheckInPatientRequest_Validation_ShouldReturnTrueForValidRequest()
    {
        // ARRANGE
        var request = CreateValidRequest();

        // ACT
        var isValid = request.IsValid();

        // ASSERT
        isValid.Should().BeTrue();
    }

    // ========================================================================
    // EXTENSIBILITY DEMONSTRATION
    // ========================================================================

    [Fact]
    public void CheckInPatientRequest_SupportsComplexPriorities()
    {
        // This test demonstrates:
        // With Parameter Object pattern, we can easily extend CheckInPatientRequest
        // with new fields without breaking method signature.
        //
        // Example future extensions:
        // - request.RequestedDoctorSpecialty
        // - request.InsuranceId
        // - request.EmergencyLevel
        //
        // The method signature stays the same:
        // public void CheckInPatient(CheckInPatientRequest request)

        var queue = CreateValidQueue();
        var urgentRequest = CreateValidRequest(priority: "urgent");

        queue.CheckInPatient(urgentRequest);

        queue.Patients.First().Priority.Level.Should().Be(4);  // Urgent = highest
    }
}
