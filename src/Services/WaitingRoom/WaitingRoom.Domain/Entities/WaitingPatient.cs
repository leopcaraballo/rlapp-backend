namespace WaitingRoom.Domain.Entities;

using ValueObjects;
using Exceptions;

/// <summary>
/// Entity representing a patient waiting in the queue.
/// Uses value objects for type safety and invariant enforcement.
/// </summary>
public sealed class WaitingPatient
{
    public PatientId PatientId { get; }
    public string PatientName { get; }
    public Priority Priority { get; }
    public ConsultationType ConsultationType { get; }
    public string? Notes { get; }
    public DateTime CheckInTime { get; }
    public int QueuePosition { get; }

    public WaitingPatient(
        PatientId patientId,
        string patientName,
        Priority priority,
        ConsultationType consultationType,
        DateTime checkInTime,
        int queuePosition,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(patientName))
            throw new DomainException("Patient name cannot be empty");

        if (checkInTime == default)
            throw new DomainException("CheckInTime must be set");

        if (queuePosition < 0)
            throw new DomainException("QueuePosition cannot be negative");

        PatientId = patientId ?? throw new ArgumentNullException(nameof(patientId));
        PatientName = patientName.Trim();
        Priority = priority ?? throw new ArgumentNullException(nameof(priority));
        ConsultationType = consultationType ?? throw new ArgumentNullException(nameof(consultationType));
        CheckInTime = checkInTime;
        QueuePosition = queuePosition;
        Notes = notes;
    }

    /// <summary>
    /// Returns wait time from check-in to given time.
    /// </summary>
    /// <param name="currentTime">Current time to calculate wait duration against.</param>
    public TimeSpan GetWaitDuration(DateTime currentTime) => currentTime - CheckInTime;
}
