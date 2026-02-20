namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.ValueObjects;

public sealed record CompleteAttentionRequest
{
    public required PatientId PatientId { get; init; }
    public required DateTime CompletedAt { get; init; }
    public required EventMetadata Metadata { get; init; }
    public string? Outcome { get; init; }
    public string? Notes { get; init; }
}
