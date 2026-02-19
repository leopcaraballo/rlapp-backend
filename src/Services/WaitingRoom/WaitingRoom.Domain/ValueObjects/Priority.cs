namespace WaitingRoom.Domain.ValueObjects;

using Exceptions;

/// <summary>
/// Value object representing patient priority levels.
/// Valid values: Low, Medium, High, Urgent.
/// </summary>
public sealed record Priority
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Urgent = "Urgent";

    private static readonly HashSet<string> ValidValues =
        [Low, Medium, High, Urgent];

    public string Value { get; }

    private Priority(string value) => Value = value;

    public static Priority Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Priority cannot be empty");

        var normalized = value.Trim();

        if (!ValidValues.Contains(normalized))
            throw new DomainException(
                $"Invalid priority '{normalized}'. Valid values: {string.Join(", ", ValidValues)}");

        return new(normalized);
    }

    /// <summary>
    /// Priority level as numeric for comparison.
    /// Higher number = higher priority.
    /// </summary>
    public int Level => Value switch
    {
        Urgent => 4,
        High => 3,
        Medium => 2,
        Low => 1,
        _ => 0
    };

    public override string ToString() => Value;
}
