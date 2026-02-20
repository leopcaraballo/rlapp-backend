namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.ValueObjects;

public sealed record CancelByPaymentRequest
{
    public required PatientId PatientId { get; init; }
    public required DateTime CancelledAt { get; init; }
    public required EventMetadata Metadata { get; init; }
    public string? Reason { get; init; }
}
