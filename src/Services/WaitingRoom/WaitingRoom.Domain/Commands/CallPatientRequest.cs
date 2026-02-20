namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.ValueObjects;

public sealed record CallPatientRequest
{
    public required PatientId PatientId { get; init; }
    public required DateTime CalledAt { get; init; }
    public required EventMetadata Metadata { get; init; }
}
