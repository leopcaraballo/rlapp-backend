namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;

public sealed record ClaimNextPatientRequest
{
    public required DateTime ClaimedAt { get; init; }
    public required EventMetadata Metadata { get; init; }
    public string? StationId { get; init; }
}
