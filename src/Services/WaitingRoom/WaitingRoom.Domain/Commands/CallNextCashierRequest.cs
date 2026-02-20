namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;

public sealed record CallNextCashierRequest
{
    public required DateTime CalledAt { get; init; }
    public required EventMetadata Metadata { get; init; }
    public string? CashierDeskId { get; init; }
}
