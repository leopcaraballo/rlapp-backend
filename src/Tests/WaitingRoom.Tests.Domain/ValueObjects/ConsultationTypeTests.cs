namespace WaitingRoom.Tests.Domain.ValueObjects;

using FluentAssertions;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Exceptions;
using Xunit;

public class ConsultationTypeTests
{
    [Fact]
    public void Create_WithValidType_ReturnsConsultationType()
    {
        // Arrange & Act
        var type = ConsultationType.Create("General");

        // Assert
        type.Value.Should().Be("General");
    }

    [Fact]
    public void Create_WithEmpty_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => ConsultationType.Create(""));
    }

    [Fact]
    public void Create_WithSingleCharacter_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => ConsultationType.Create("A"));
    }

    [Fact]
    public void Create_WithTooLong_ThrowsDomainException()
    {
        // Arrange
        var longString = new string('A', 101);

        // Act & Assert
        Assert.Throws<DomainException>(() => ConsultationType.Create(longString));
    }

    [Fact]
    public void Create_WithWhitespace_Trims()
    {
        // Arrange & Act
        var type = ConsultationType.Create("  Cardiology  ");

        // Assert
        type.Value.Should().Be("Cardiology");
    }
}
