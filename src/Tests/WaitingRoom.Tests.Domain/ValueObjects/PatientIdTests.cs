namespace WaitingRoom.Tests.Domain.ValueObjects;

using FluentAssertions;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Exceptions;
using Xunit;

public class PatientIdTests
{
    [Fact]
    public void Create_WithValidId_ReturnsPatientId()
    {
        // Arrange & Act
        var patientId = PatientId.Create("PAT-001");

        // Assert
        patientId.Value.Should().Be("PAT-001");
    }

    [Fact]
    public void Create_WithWhitespace_TrimsMutex()
    {
        // Arrange & Act
        var patientId = PatientId.Create("  PAT-001  ");

        // Assert
        patientId.Value.Should().Be("PAT-001");
    }

    [Fact]
    public void Create_WithEmptyString_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => PatientId.Create(""));
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => PatientId.Create("   "));
    }

    [Fact]
    public void Create_WithNull_ThrowsDomainException()
    {
        // Arrange & Act & Assert
        Assert.Throws<DomainException>(() => PatientId.Create(null!));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var id1 = PatientId.Create("PAT-001");
        var id2 = PatientId.Create("PAT-001");

        // Act & Assert
        id1.Should().Be(id2);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        // Arrange
        var id1 = PatientId.Create("PAT-001");
        var id2 = PatientId.Create("PAT-002");

        // Act & Assert
        id1.Should().NotBe(id2);
    }
}
