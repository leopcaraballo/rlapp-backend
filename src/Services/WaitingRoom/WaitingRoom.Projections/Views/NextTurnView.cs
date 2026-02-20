namespace WaitingRoom.Projections.Views;

public sealed record NextTurnView
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required string ConsultationType { get; init; }
    public required string Status { get; init; }
    public DateTime? ClaimedAt { get; init; }
    public DateTime? CalledAt { get; init; }
    public string? StationId { get; init; }
    public required DateTimeOffset ProjectedAt { get; init; }
}
