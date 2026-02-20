namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.ValueObjects;

public sealed record MarkAbsentAtCashierRequest
{
    public required PatientId PatientId { get; init; }
    public required DateTime AbsentAt { get; init; }
    public required EventMetadata Metadata { get; init; }
}
