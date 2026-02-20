namespace WaitingRoom.Tests.Domain.ValueObjects;

using FluentAssertions;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Exceptions;
using Xunit;

public class PriorityTests
{
    [Theory]
    [InlineData(Priority.Low, 1)]
    [InlineData(Priority.Medium, 2)]
    [InlineData(Priority.High, 3)]
    [InlineData(Priority.Urgent, 4)]
    public void Create_WithValidPriority_ReturnsPriority(string priorityValue, int expectedLevel)
    {
        // Arrange & Act
        var priority = Priority.Create(priorityValue);

        // Assert
        priority.Value.Should().Be(priorityValue);
        priority.Level.Should().Be(expectedLevel);
    }

    [Fact]
    public void Create_WithInvalidPriority_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => Priority.Create("Invalid"));
    }

    [Fact]
    public void Create_WithEmpty_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => Priority.Create(""));
    }

    [Fact]
    public void Create_WithNull_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => Priority.Create(null!));
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => Priority.Create("   "));
    }

    [Fact]
    public void Create_WithWhitespace_Trims()
    {
        // Arrange & Act
        var priority = Priority.Create("  High  ");

        // Assert
        priority.Value.Should().Be("High");
    }

    [Fact]
    public void Level_HigherPriority_HasHigherLevel()
    {
        // Arrange
        var low = Priority.Create(Priority.Low);
        var urgent = Priority.Create(Priority.Urgent);

        // Act & Assert
        urgent.Level.Should().BeGreaterThan(low.Level);
    }
}
