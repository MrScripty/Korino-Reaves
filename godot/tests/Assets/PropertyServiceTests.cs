// Property Service Tests
//
// Unit tests for PropertyService functionality.

using System;
using FluentAssertions;
using NSubstitute;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Assets;

public class PropertyServiceTests
{
    private readonly IAppLogger _logger;
    private readonly PropertyService _propertyService;

    public PropertyServiceTests()
    {
        _logger = Substitute.For<IAppLogger>();
        _logger.BeginScope(Arg.Any<string>()).Returns(Substitute.For<IDisposable>());
        _propertyService = new PropertyService(_logger);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new PropertyService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}

public class PropertyTypesTests
{
    [Fact]
    public void PropertyTypes_HasExpectedValues()
    {
        PropertyTypes.String.Should().Be("string");
        PropertyTypes.Number.Should().Be("number");
        PropertyTypes.Bool.Should().Be("bool");
        PropertyTypes.Vector.Should().Be("vector");
        PropertyTypes.Color.Should().Be("color");
        PropertyTypes.Enum.Should().Be("enum");
        PropertyTypes.Object.Should().Be("object");
        PropertyTypes.Struct.Should().Be("struct");
        PropertyTypes.Array.Should().Be("array");
        PropertyTypes.Map.Should().Be("map");
        PropertyTypes.Set.Should().Be("set");
        PropertyTypes.Byte.Should().Be("byte");
        PropertyTypes.Guid.Should().Be("guid");
        PropertyTypes.Unknown.Should().Be("unknown");
    }
}

public class AssetExceptionTests
{
    [Fact]
    public void AssetLoadException_ContainsMessage()
    {
        // Arrange
        var message = "Test error message";

        // Act
        var exception = new AssetLoadException(message);

        // Assert
        exception.Message.Should().Be(message);
    }

    [Fact]
    public void AssetLoadException_ContainsInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner");
        var message = "Outer";

        // Act
        var exception = new AssetLoadException(message, inner);

        // Assert
        exception.Message.Should().Be(message);
        exception.InnerException.Should().Be(inner);
    }

    [Fact]
    public void PropertyNotFoundException_ContainsPath()
    {
        // Arrange
        var path = new[] { "export-0", "Health" };

        // Act
        var exception = new PropertyNotFoundException(path);

        // Assert
        exception.Path.Should().BeEquivalentTo(path);
        exception.Message.Should().Contain("export-0/Health");
    }

    [Fact]
    public void InvalidPropertyValueException_ContainsPathAndValue()
    {
        // Arrange
        var path = new[] { "export-0", "Health" };
        var value = "not a number";
        var reason = "Expected integer";

        // Act
        var exception = new InvalidPropertyValueException(path, value, reason);

        // Assert
        exception.Path.Should().BeEquivalentTo(path);
        exception.Value.Should().Be(value);
        exception.Message.Should().Contain("export-0/Health");
        exception.Message.Should().Contain(reason);
    }
}
