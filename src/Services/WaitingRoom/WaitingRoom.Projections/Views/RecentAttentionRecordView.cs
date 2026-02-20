namespace WaitingRoom.Projections.Views;

public sealed record RecentAttentionRecordView
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required string ConsultationType { get; init; }
    public required DateTime CompletedAt { get; init; }
    public string? Outcome { get; init; }
    public string? Notes { get; init; }
}
