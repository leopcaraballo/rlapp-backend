namespace WaitingRoom.Application.DTOs;

public sealed record CallNextCashierDto
{
    public required string QueueId { get; init; }
    public required string Actor { get; init; }
    public string? CashierDeskId { get; init; }
}
