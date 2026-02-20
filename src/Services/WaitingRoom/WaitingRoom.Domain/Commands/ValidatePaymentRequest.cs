namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.ValueObjects;

public sealed record ValidatePaymentRequest
{
    public required PatientId PatientId { get; init; }
    public required DateTime ValidatedAt { get; init; }
    public required EventMetadata Metadata { get; init; }
    public string? PaymentReference { get; init; }
}
